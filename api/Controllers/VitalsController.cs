using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Hubs;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api/vitals"), Authorize(Roles = $"{Roles.Patient},{Roles.Doctor},{Roles.Nurse}")]
public class VitalsController(AppDbContext db, NotificationService notify, IHubContext<NotificationsHub> hub,
    AuditService audit) : ControllerBase
{
    private static VitalsDto ToDto(VitalSign v, string patientName, string byName) => new(v.Id, v.PatientId, patientName,
        byName, v.RecordedAt, v.HeartRate, v.Systolic, v.Diastolic, v.SpO2, v.TemperatureC, v.Severity, v.AlertReason);

    /// <summary>Nurses may record for any patient. Doctors only for patients they have an appointment with.</summary>
    [HttpPost, Authorize(Roles = $"{Roles.Doctor},{Roles.Nurse}")]
    public async Task<ActionResult<VitalsDto>> Create(CreateVitalsRequest req)
    {
        if (req.Diastolic >= req.Systolic)
            return BadRequest(new { error = "Diastolic pressure must be lower than systolic." });

        var uid = User.UserId();
        var isNurse = User.RoleName() == Roles.Nurse;
        var patient = await db.Patients.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == req.PatientId
            && (isNurse || db.Appointments.Any(a => a.PatientId == p.Id && a.Doctor.UserId == uid)));
        if (patient is null) return NotFound();

        var me = await db.Users.AsNoTracking().FirstAsync(u => u.Id == uid);
        var (severity, reason) = VitalsRules.Evaluate(req.HeartRate, req.Systolic, req.Diastolic, req.SpO2, req.TemperatureC);
        var vital = new VitalSign
        {
            PatientId = patient.Id, RecordedByUserId = uid, HeartRate = req.HeartRate, Systolic = req.Systolic,
            Diastolic = req.Diastolic, SpO2 = req.SpO2, TemperatureC = req.TemperatureC,
            Severity = severity, AlertReason = reason,
        };
        db.VitalSigns.Add(vital);
        await db.SaveChangesAsync();
        await audit.LogAsync("CreateVitals", "VitalSign", vital.Id.ToString());

        var dto = ToDto(vital, patient.User.FullName, me.FullName);

        // Live update for everyone allowed to see this patient's data.
        var recipients = (await notify.DoctorIdsFor(patient.Id)).Concat(await notify.NurseIds()).Distinct().ToList();
        foreach (var id in recipients.Where(i => i != uid))
            await hub.Clients.User(id.ToString()).SendAsync("vitalsRecorded", dto);

        if (severity != Severity.Normal)
            await notify.SendAsync(recipients, NotificationType.Vitals,
                $"{severity.ToUpperInvariant()}: {patient.User.FullName} — {reason}", exceptUserId: uid);

        return StatusCode(201, dto);
    }

    [HttpGet]
    public async Task<ActionResult<List<VitalsDto>>> List(Guid? patientId)
    {
        var uid = User.UserId();
        var role = User.RoleName();
        IQueryable<VitalSign> q = db.VitalSigns.AsNoTracking();

        if (role == Roles.Patient)
            q = q.Where(v => v.Patient.UserId == uid);                       // always own data, whatever id is sent
        else
        {
            if (patientId is null) return BadRequest(new { error = "patientId is required." });
            q = role == Roles.Nurse
                ? q.Where(v => v.PatientId == patientId)
                : q.Where(v => v.PatientId == patientId
                    && db.Appointments.Any(a => a.PatientId == v.PatientId && a.Doctor.UserId == uid));
        }

        var list = await q.OrderByDescending(v => v.RecordedAt).Take(50)
            .Select(v => ToDto(v, v.Patient.User.FullName, v.RecordedBy.FullName)).ToListAsync();
        await audit.LogAsync("ReadVitals", "VitalSign", patientId?.ToString());
        return list;
    }
}
