using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api/prescriptions"), Authorize]
public class PrescriptionsController(AppDbContext db, BillingService billing, NotificationService notify,
    AttendanceClock clock, AuditService audit) : ControllerBase
{
    private IQueryable<Prescription> WithDetails() => db.Prescriptions
        .Include(p => p.Patient.User).Include(p => p.Doctor.User).Include(p => p.Items).ThenInclude(i => i.Medicine);

    private static PrescriptionDto ToDto(Prescription p) => new(p.Id, p.PatientId, p.Patient.User.FullName, p.Doctor.User.FullName,
        p.CreatedAt, p.Status, p.Notes,
        p.Items.Select(i => new PrescriptionItemDto(i.MedicineId, i.Medicine.Name, i.Medicine.Strength, i.Dosage, i.DurationDays, i.Quantity)).ToList());

    /// <summary>Patients see their own, doctors the ones they wrote, pharmacists all. Nobody else (not even admins).</summary>
    [HttpGet, Authorize(Roles = $"{Roles.Patient},{Roles.Doctor},{Roles.Pharmacist}")]
    public async Task<PagedResult<PrescriptionDto>> List(string? status, string? q, int page = 1, int pageSize = 20)
    {
        var uid = User.UserId();
        var query = WithDetails().AsNoTracking();
        query = User.RoleName() switch
        {
            Roles.Patient => query.Where(p => p.Patient.UserId == uid),
            Roles.Doctor => query.Where(p => p.Doctor.UserId == uid),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(p => p.Status == status);
        if (PagingExtensions.CleanSearch(q) is { } term)
            query = query.Where(p => p.Patient.User.FullName.Contains(term) || p.Items.Any(i => i.Medicine.Name.Contains(term)));

        var result = (await query.OrderByDescending(p => p.CreatedAt).ToPagedAsync(page, pageSize)).Map(ToDto);
        await audit.LogAsync("ReadPrescriptions", "Prescription");
        return result;
    }

    [HttpPost, Authorize(Roles = Roles.Doctor)]
    public async Task<ActionResult<PrescriptionDto>> Create(CreatePrescriptionRequest req)
    {
        var uid = User.UserId();
        var doctor = await db.Doctors.Include(d => d.User).FirstAsync(d => d.UserId == uid);
        var patient = await db.Patients.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == req.PatientId && db.Appointments.Any(a => a.PatientId == p.Id && a.DoctorId == doctor.Id));
        if (patient is null) return NotFound(); // same answer for "no such patient" and "not your patient"

        var ids = req.Items.Select(i => i.MedicineId).ToList();
        if (ids.Distinct().Count() != ids.Count) return BadRequest(new { error = "Each medicine can appear only once." });
        var medicines = await db.Medicines.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
        if (medicines.Count != ids.Count) return BadRequest(new { error = "One of the medicines does not exist." });

        var today = clock.Today();
        if (medicines.Values.FirstOrDefault(m => m.ExpiryDate < today) is { } expired)
            return BadRequest(new { error = $"{expired.Name} has expired and cannot be prescribed." });

        // Safety check: compare the patient's recorded allergies with what is being prescribed.
        var allergens = patient.Allergies.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => a.Length >= 3).ToList();
        var conflicts = medicines.Values
            .Where(m => allergens.Any(a => m.Name.Contains(a, StringComparison.OrdinalIgnoreCase) || a.Contains(m.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Name).ToList();
        if (conflicts.Count > 0 && !req.OverrideAllergyWarning)
            return Conflict(new
            {
                error = $"Allergy warning: {patient.User.FullName} has a recorded allergy that matches {string.Join(", ", conflicts)}. Tick the override box to prescribe anyway.",
                allergyConflict = true,
            });

        var rx = new Prescription { PatientId = patient.Id, DoctorId = doctor.Id, Notes = req.Notes.Trim() };
        foreach (var i in req.Items)
            rx.Items.Add(new PrescriptionItem { MedicineId = i.MedicineId, Dosage = i.Dosage.Trim(), DurationDays = i.DurationDays, Quantity = i.Quantity });
        db.Prescriptions.Add(rx);
        await db.SaveChangesAsync();
        await audit.LogAsync(conflicts.Count > 0 ? "PrescribeAllergyOverride" : "CreatePrescription", "Prescription", rx.Id.ToString());

        await notify.SendAsync([patient.UserId], NotificationType.Appointment, $"{doctor.User.FullName} wrote you a new prescription.");
        await notify.SendAsync(await notify.UserIdsInRole(Roles.Pharmacist), NotificationType.Appointment,
            $"New prescription to dispense for {patient.User.FullName}.");
        return StatusCode(201, ToDto(await WithDetails().AsNoTracking().FirstAsync(p => p.Id == rx.Id)));
    }

    /// <summary>
    /// Hands the medicines over: claims the prescription, takes stock, and bills the patient, all or nothing.
    /// The claim and the stock change are single SQL statements, so two pharmacists clicking at once can't dispense twice
    /// or sell stock that isn't there.
    /// </summary>
    [HttpPost("{id:guid}/dispense"), Authorize(Roles = Roles.Pharmacist)]
    public async Task<ActionResult<PrescriptionDto>> Dispense(Guid id)
    {
        var uid = User.UserId();
        var rx = await WithDetails().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (rx is null) return NotFound();
        if (rx.Status != PrescriptionStatus.Pending) return Conflict(new { error = $"This prescription is already {rx.Status}." });

        var today = clock.Today();
        if (rx.Items.FirstOrDefault(i => i.Medicine.ExpiryDate < today) is { } old)
            return Conflict(new { error = $"{old.Medicine.Name} has expired and cannot be dispensed." });

        await using var tx = await db.Database.BeginTransactionAsync();
        var now = DateTimeOffset.UtcNow;
        var claimed = await db.Prescriptions.Where(p => p.Id == id && p.Status == PrescriptionStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PrescriptionStatus.Dispensed)
                .SetProperty(p => p.DispensedByUserId, uid).SetProperty(p => p.DispensedAt, now));
        if (claimed == 0)
        {
            await tx.RollbackAsync();
            return Conflict(new { error = "Someone else just dispensed this prescription." });
        }

        foreach (var item in rx.Items)
        {
            var qty = item.Quantity;
            var taken = await db.Medicines.Where(m => m.Id == item.MedicineId && m.Stock >= qty)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Stock, m => m.Stock - qty));
            if (taken == 0)
            {
                await tx.RollbackAsync();
                return Conflict(new { error = $"Not enough stock of {item.Medicine.Name}." });
            }
            await billing.AddChargeAsync(rx.PatientId, InvoiceSource.Medicine,
                $"{item.Medicine.Name} {item.Medicine.Strength} x{qty}".Replace("  ", " "), qty, item.Medicine.UnitPrice);
        }
        await tx.CommitAsync();
        await audit.LogAsync("DispensePrescription", "Prescription", id.ToString());

        await notify.SendAsync([rx.Patient.UserId], NotificationType.Appointment, "Your prescription has been dispensed.");
        var ids = rx.Items.Select(i => i.MedicineId).ToList();
        var low = await db.Medicines.AsNoTracking().Where(m => ids.Contains(m.Id) && m.Stock <= m.ReorderLevel).ToListAsync();
        foreach (var m in low)
            await notify.SendAsync(await notify.UserIdsInRole(Roles.Pharmacist), NotificationType.Appointment,
                $"Low stock: {m.Name} has {m.Stock} left (reorder level {m.ReorderLevel}).");

        return ToDto(await WithDetails().AsNoTracking().FirstAsync(p => p.Id == id));
    }

    [HttpPost("{id:guid}/cancel"), Authorize(Roles = Roles.Doctor)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var uid = User.UserId();
        var rows = await db.Prescriptions.Where(p => p.Id == id && p.Doctor.UserId == uid && p.Status == PrescriptionStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PrescriptionStatus.Cancelled));
        if (rows == 0) return NotFound();
        await audit.LogAsync("CancelPrescription", "Prescription", id.ToString());
        return NoContent();
    }
}
