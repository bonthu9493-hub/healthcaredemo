using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Hubs;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Authorize]
public class WardsController(AppDbContext db, BillingService billing, NotificationService notify,
    IHubContext<NotificationsHub> hub, AuditService audit) : ControllerBase
{
    // Like the queue, this signal carries no data; screens re-fetch the board through the authorised endpoint.
    private Task Broadcast() => hub.Clients.All.SendAsync("bedsChanged");

    /// <summary>The ward board: every bed and who is in it. Clinical staff and admins only.</summary>
    [HttpGet("api/wards"), Authorize(Roles = $"{Roles.Doctor},{Roles.Nurse},{Roles.Admin}")]
    public async Task<List<WardDto>> Board()
    {
        var wards = await db.Wards.AsNoTracking().Include(w => w.Beds).OrderBy(w => w.Name).ToListAsync();
        var active = await db.Admissions.AsNoTracking().Where(a => a.DischargedAt == null)
            .Select(a => new { a.Id, a.BedId, a.PatientId, Name = a.Patient.User.FullName, a.AdmittedAt }).ToListAsync();
        var byBed = active.ToDictionary(a => a.BedId);

        return wards.Select(w =>
        {
            var beds = w.Beds.OrderBy(b => b.Label).Select(b => byBed.TryGetValue(b.Id, out var a)
                ? new BedDto(b.Id, b.Label, a.Id, a.PatientId, a.Name, a.AdmittedAt)
                : new BedDto(b.Id, b.Label, null, null, null, null)).ToList();
            return new WardDto(w.Id, w.Name, w.Type, w.DailyRate, beds.Count, beds.Count(b => b.AdmissionId != null), beds);
        }).ToList();
    }

    [HttpPost("api/wards"), Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<WardDto>> CreateWard(CreateWardRequest req)
    {
        var name = req.Name.Trim();
        if (await db.Wards.AnyAsync(w => w.Name == name)) return Conflict(new { error = "A ward with that name already exists." });
        var ward = new Ward { Name = name, Type = req.Type, DailyRate = req.DailyRate };
        db.Wards.Add(ward);
        await db.SaveChangesAsync();
        await audit.LogAsync("CreateWard", "Ward", ward.Id.ToString());
        await Broadcast();
        return StatusCode(201, new WardDto(ward.Id, ward.Name, ward.Type, ward.DailyRate, 0, 0, []));
    }

    [HttpPost("api/wards/{id:guid}/beds"), Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<BedDto>> AddBed(Guid id, CreateBedRequest req)
    {
        if (!await db.Wards.AnyAsync(w => w.Id == id)) return NotFound();
        var label = req.Label.Trim();
        if (await db.Beds.AnyAsync(b => b.WardId == id && b.Label == label)) return Conflict(new { error = "That bed already exists in this ward." });
        var bed = new Bed { WardId = id, Label = label };
        db.Beds.Add(bed);
        await db.SaveChangesAsync();
        await audit.LogAsync("CreateBed", "Bed", bed.Id.ToString());
        await Broadcast();
        return StatusCode(201, new BedDto(bed.Id, bed.Label, null, null, null, null));
    }

    /// <summary>
    /// Admits a patient to a bed. The database itself allows only one active admission per bed and per patient,
    /// so two people admitting at the same moment can't double-book a bed.
    /// </summary>
    [HttpPost("api/admissions"), Authorize(Roles = $"{Roles.Doctor},{Roles.Nurse}")]
    public async Task<ActionResult<AdmissionDto>> Admit(AdmitRequest req)
    {
        var uid = User.UserId();
        var isNurse = User.RoleName() == Roles.Nurse;
        var patient = await db.Patients.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == req.PatientId
            && (isNurse || db.Appointments.Any(a => a.PatientId == p.Id && a.Doctor.UserId == uid)));
        if (patient is null) return NotFound();
        var bed = await db.Beds.Include(b => b.Ward).FirstOrDefaultAsync(b => b.Id == req.BedId);
        if (bed is null) return BadRequest(new { error = "Unknown bed." });

        var admission = new Admission { PatientId = patient.Id, BedId = bed.Id, AdmittedByUserId = uid, Reason = req.Reason.Trim() };
        db.Admissions.Add(admission);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            var taken = await db.Admissions.AnyAsync(a => a.BedId == bed.Id && a.DischargedAt == null);
            return Conflict(new { error = taken ? "That bed is already occupied." : "This patient is already admitted." });
        }
        await audit.LogAsync("AdmitPatient", "Admission", admission.Id.ToString());
        await notify.SendAsync([patient.UserId], NotificationType.Appointment, $"You have been admitted to {bed.Ward.Name}, {bed.Label}.");
        await Broadcast();
        return StatusCode(201, new AdmissionDto(admission.Id, bed.Ward.Name, bed.Label, admission.Reason, admission.AdmittedAt, null, null));
    }

    /// <summary>Discharge needs a summary, frees the bed, and adds the bed charge (days x daily rate) to the patient's invoice.</summary>
    [HttpPost("api/admissions/{id:guid}/discharge"), Authorize(Roles = Roles.Doctor)]
    public async Task<ActionResult<AdmissionDto>> Discharge(Guid id, DischargeRequest req)
    {
        var uid = User.UserId();
        var now = DateTimeOffset.UtcNow;
        var summary = req.Summary.Trim();

        await using var tx = await db.Database.BeginTransactionAsync();
        var rows = await db.Admissions.Where(a => a.Id == id && a.DischargedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DischargedAt, now).SetProperty(a => a.DischargedByUserId, uid)
                .SetProperty(a => a.DischargeSummary, summary));
        if (rows == 0)
        {
            await tx.RollbackAsync();
            return await db.Admissions.AnyAsync(a => a.Id == id) ? Conflict(new { error = "This patient has already been discharged." }) : NotFound();
        }

        var a = await db.Admissions.AsNoTracking().Include(x => x.Patient.User).Include(x => x.Bed.Ward).FirstAsync(x => x.Id == id);
        var days = Math.Max(1, (int)Math.Ceiling((now - a.AdmittedAt).TotalDays));
        await billing.AddChargeAsync(a.PatientId, InvoiceSource.Bed, $"Bed charge: {a.Bed.Ward.Name}, {a.Bed.Label} ({days} day{(days == 1 ? "" : "s")})",
            days, a.Bed.Ward.DailyRate);
        await tx.CommitAsync();
        await audit.LogAsync("DischargePatient", "Admission", id.ToString());

        await notify.SendAsync([a.Patient.UserId], NotificationType.Appointment, "You have been discharged. Your discharge summary is in your admissions list.");
        await Broadcast();
        return new AdmissionDto(a.Id, a.Bed.Ward.Name, a.Bed.Label, a.Reason, a.AdmittedAt, a.DischargedAt, a.DischargeSummary);
    }

    /// <summary>A patient's own admissions, current and past.</summary>
    [HttpGet("api/admissions/me"), Authorize(Roles = Roles.Patient)]
    public async Task<List<AdmissionDto>> Mine()
    {
        var uid = User.UserId();
        return await db.Admissions.AsNoTracking().Where(a => a.Patient.UserId == uid).OrderByDescending(a => a.AdmittedAt)
            .Select(a => new AdmissionDto(a.Id, a.Bed.Ward.Name, a.Bed.Label, a.Reason, a.AdmittedAt, a.DischargedAt, a.DischargeSummary)).ToListAsync();
    }
}
