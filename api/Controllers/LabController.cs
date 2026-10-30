using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api/labs"), Authorize]
public class LabController(AppDbContext db, BillingService billing, NotificationService notify, AuditService audit) : ControllerBase
{
    private static LabOrderDto ToDto(LabOrder o) => new(o.Id, o.PatientId, o.Patient.User.FullName, o.Doctor.User.FullName,
        o.LabTest.Name, o.Price, o.Notes, o.Status, o.OrderedAt, o.ResultText, o.Abnormal, o.CompletedAt);

    private IQueryable<LabOrder> WithDetails() =>
        db.LabOrders.Include(o => o.Patient.User).Include(o => o.Doctor.User).Include(o => o.LabTest);

    // ---------- catalogue of tests ----------

    [HttpGet("tests"), Authorize(Roles = $"{Roles.Doctor},{Roles.LabTech},{Roles.Admin}")]
    public async Task<List<LabTestDto>> Tests() =>
        await db.LabTests.AsNoTracking().OrderBy(t => t.Name).Select(t => new LabTestDto(t.Id, t.Name, t.Price)).ToListAsync();

    [HttpPost("tests"), Authorize(Roles = $"{Roles.LabTech},{Roles.Admin}")]
    public async Task<ActionResult<LabTestDto>> AddTest(CreateLabTestRequest req)
    {
        var name = req.Name.Trim();
        if (await db.LabTests.AnyAsync(t => t.Name == name)) return Conflict(new { error = "That test already exists." });
        var t = new LabTest { Name = name, Price = req.Price };
        db.LabTests.Add(t);
        await db.SaveChangesAsync();
        await audit.LogAsync("CreateLabTest", "LabTest", t.Id.ToString());
        return StatusCode(201, new LabTestDto(t.Id, t.Name, t.Price));
    }

    // ---------- orders ----------

    /// <summary>Patients see their own, doctors the ones they ordered, lab technicians all.</summary>
    [HttpGet("orders"), Authorize(Roles = $"{Roles.Patient},{Roles.Doctor},{Roles.LabTech}")]
    public async Task<PagedResult<LabOrderDto>> Orders(string? status, string? q, int page = 1, int pageSize = 20)
    {
        var uid = User.UserId();
        var query = WithDetails().AsNoTracking();
        query = User.RoleName() switch
        {
            Roles.Patient => query.Where(o => o.Patient.UserId == uid),
            Roles.Doctor => query.Where(o => o.Doctor.UserId == uid),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(o => o.Status == status);
        if (PagingExtensions.CleanSearch(q) is { } term)
            query = query.Where(o => o.LabTest.Name.Contains(term) || o.Patient.User.FullName.Contains(term));

        var result = (await query.OrderByDescending(o => o.OrderedAt).ToPagedAsync(page, pageSize)).Map(ToDto);
        await audit.LogAsync("ReadLabOrders", "LabOrder");
        return result;
    }

    [HttpPost("orders"), Authorize(Roles = Roles.Doctor)]
    public async Task<ActionResult<LabOrderDto>> Order(OrderLabRequest req)
    {
        var uid = User.UserId();
        var doctor = await db.Doctors.Include(d => d.User).FirstAsync(d => d.UserId == uid);
        var patient = await db.Patients.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == req.PatientId && db.Appointments.Any(a => a.PatientId == p.Id && a.DoctorId == doctor.Id));
        if (patient is null) return NotFound();
        var test = await db.LabTests.FirstOrDefaultAsync(t => t.Id == req.LabTestId);
        if (test is null) return BadRequest(new { error = "Unknown lab test." });

        var order = new LabOrder { PatientId = patient.Id, DoctorId = doctor.Id, LabTestId = test.Id, Price = test.Price, Notes = req.Notes.Trim() };
        db.LabOrders.Add(order);
        await db.SaveChangesAsync();
        await audit.LogAsync("OrderLab", "LabOrder", order.Id.ToString());
        await notify.SendAsync(await notify.UserIdsInRole(Roles.LabTech), NotificationType.Appointment,
            $"New lab order: {test.Name} for {patient.User.FullName}.");
        return StatusCode(201, ToDto(await WithDetails().AsNoTracking().FirstAsync(o => o.Id == order.Id)));
    }

    /// <summary>Enters the result. The status flip is one SQL statement, so a test can only be completed (and billed) once.</summary>
    [HttpPost("orders/{id:guid}/result"), Authorize(Roles = Roles.LabTech)]
    public async Task<ActionResult<LabOrderDto>> Result(Guid id, LabResultRequest req)
    {
        var uid = User.UserId();
        var now = DateTimeOffset.UtcNow;
        var text = req.ResultText.Trim();
        await using var tx = await db.Database.BeginTransactionAsync();
        var rows = await db.LabOrders.Where(o => o.Id == id && o.Status == LabStatus.Ordered)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, LabStatus.Completed).SetProperty(o => o.ResultText, text)
                .SetProperty(o => o.Abnormal, req.Abnormal).SetProperty(o => o.CompletedByUserId, uid).SetProperty(o => o.CompletedAt, now));
        if (rows == 0)
        {
            await tx.RollbackAsync();
            return await db.LabOrders.AnyAsync(o => o.Id == id) ? Conflict(new { error = "This order is not waiting for a result." }) : NotFound();
        }

        var order = await WithDetails().AsNoTracking().FirstAsync(o => o.Id == id);
        await billing.AddChargeAsync(order.PatientId, InvoiceSource.Lab, order.LabTest.Name, 1, order.Price);
        await tx.CommitAsync();
        await audit.LogAsync("CompleteLabOrder", "LabOrder", id.ToString());

        await notify.SendAsync([order.Patient.UserId], NotificationType.Appointment, $"Your {order.LabTest.Name} result is ready.");
        await notify.SendAsync([order.Doctor.UserId], NotificationType.Vitals,
            $"{order.Patient.User.FullName}: {order.LabTest.Name} result is ready{(req.Abnormal ? " — ABNORMAL" : "")}.");
        return ToDto(order);
    }

    [HttpPost("orders/{id:guid}/cancel"), Authorize(Roles = Roles.Doctor)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var uid = User.UserId();
        var rows = await db.LabOrders.Where(o => o.Id == id && o.Doctor.UserId == uid && o.Status == LabStatus.Ordered)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, LabStatus.Cancelled));
        if (rows == 0) return NotFound();
        await audit.LogAsync("CancelLabOrder", "LabOrder", id.ToString());
        return NoContent();
    }
}
