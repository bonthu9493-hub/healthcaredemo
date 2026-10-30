using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Hubs;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api/appointments"), Authorize]
public class AppointmentsController(AppDbContext db, IHubContext<NotificationsHub> hub, AuditService audit,
    AttendanceClock clock, NotificationService notify, BillingService billing) : ControllerBase
{
    private IQueryable<Appointment> Visible()
    {
        var uid = User.UserId();
        var q = db.Appointments.AsNoTracking();
        return User.RoleName() switch
        {
            Roles.Admin or Roles.Nurse => q,
            Roles.Doctor => q.Where(a => a.Doctor.UserId == uid),
            _ => q.Where(a => a.Patient.UserId == uid),
        };
    }

    private static AppointmentDto ToDto(Appointment a) => new(a.Id, a.PatientId, a.Patient.User.FullName,
        a.DoctorId, a.Doctor.User.FullName, a.Doctor.Specialty, a.StartsAt, a.Reason, a.Status);

    /// <summary>Newest first. Filtering and paging happen in SQL, on top of the "only what I may see" scope.</summary>
    [HttpGet]
    public async Task<PagedResult<AppointmentDto>> List(string? q, string? status, int page = 1, int pageSize = 20)
    {
        var query = Visible();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);
        if (PagingExtensions.CleanSearch(q) is { } term)
            query = query.Where(a => a.Patient.User.FullName.Contains(term) || a.Doctor.User.FullName.Contains(term)
                || a.Reason.Contains(term));
        return await query.OrderByDescending(a => a.StartsAt)
            .Select(a => new AppointmentDto(a.Id, a.PatientId, a.Patient.User.FullName, a.DoctorId, a.Doctor.User.FullName,
                a.Doctor.Specialty, a.StartsAt, a.Reason, a.Status))
            .ToPagedAsync(page, pageSize);
    }

    [HttpPost, Authorize(Roles = Roles.Patient)]
    public async Task<ActionResult<AppointmentDto>> Create(CreateAppointmentRequest req)
    {
        if (req.StartsAt <= DateTimeOffset.UtcNow)
            return BadRequest(new { error = "Appointment must be in the future." });

        var uid = User.UserId();
        var patient = await db.Patients.Include(p => p.User).FirstAsync(p => p.UserId == uid);
        var doctor = await db.Doctors.Include(d => d.User).FirstOrDefaultAsync(d => d.Id == req.DoctorId);
        if (doctor is null) return BadRequest(new { error = "Doctor not found." });

        var start = req.StartsAt.ToUniversalTime();

        // If the doctor publishes a schedule, the time must be exactly one of its slots.
        var schedule = await db.DoctorSchedules.AsNoTracking().Where(s => s.DoctorId == doctor.Id).ToListAsync();
        var slotMinutes = 30;
        if (schedule.Count > 0)
        {
            var match = Scheduling.MatchingEntry(schedule, clock.ToLocal(start));
            if (match is null) return BadRequest(new { error = "Please choose one of the doctor's available slots." });
            slotMinutes = match.SlotMinutes;
        }

        var taken = await db.Appointments.AnyAsync(a => a.DoctorId == doctor.Id
            && a.Status == AppointmentStatus.Scheduled
            && a.StartsAt > start.AddMinutes(-slotMinutes) && a.StartsAt < start.AddMinutes(slotMinutes));
        if (taken) return Conflict(new { error = "That time slot is not available." });

        var appt = new Appointment
        {
            Patient = patient, Doctor = doctor, StartsAt = start, Reason = req.Reason.Trim(),
        };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();
        await audit.LogAsync("CreateAppointment", "Appointment", appt.Id.ToString());

        var dto = ToDto(appt);
        await hub.Clients.User(doctor.UserId.ToString()).SendAsync("appointmentCreated", dto);
        await notify.SendAsync([doctor.UserId], NotificationType.Appointment,
            $"New appointment: {patient.User.FullName} on {clock.ToLocal(start):dd MMM, h:mm tt} — {appt.Reason}");
        return StatusCode(201, dto);
    }

    [HttpPatch("{id:guid}/status"), Authorize(Roles = $"{Roles.Patient},{Roles.Doctor},{Roles.Admin}")]
    public async Task<ActionResult<AppointmentDto>> UpdateStatus(Guid id, UpdateStatusRequest req)
    {
        if (!AppointmentStatus.All.Contains(req.Status))
            return BadRequest(new { error = "Invalid status." });

        // Scoped by Visible(): a user can only ever touch appointments they are part of.
        // Two separate queries on purpose: the AsNoTracking inside Visible() would make a combined query
        // untracked, and the status change below would silently not be saved.
        if (!await Visible().AnyAsync(a => a.Id == id)) return NotFound();
        var appt = await db.Appointments.Include(a => a.Patient.User).Include(a => a.Doctor.User)
            .FirstAsync(a => a.Id == id);

        // Patients may only cancel; doctors/admins may set any status.
        if (User.RoleName() == Roles.Patient && req.Status != AppointmentStatus.Cancelled)
            return Forbid();

        // Attendance history must stay trustworthy: only a Scheduled appointment can change,
        // and a no-show can only be recorded once the appointment time has passed.
        if (appt.Status != AppointmentStatus.Scheduled)
            return Conflict(new { error = $"This appointment is already {appt.Status} and can't be changed." });
        if (req.Status == AppointmentStatus.NoShow && appt.StartsAt > DateTimeOffset.UtcNow)
            return BadRequest(new { error = "A future appointment can't be marked as a no-show." });
        if (req.Status == AppointmentStatus.Scheduled)
            return BadRequest(new { error = "Invalid status." });

        appt.Status = req.Status;
        await db.SaveChangesAsync();
        // An attended appointment is a billable consultation. (Only a Scheduled appointment can change, so this runs once.)
        if (req.Status == AppointmentStatus.Completed)
            await billing.AddChargeAsync(appt.PatientId, InvoiceSource.Consultation,
                $"Consultation: {appt.Doctor.User.FullName} ({appt.Doctor.Specialty})", 1, appt.Doctor.ConsultationFee);
        await audit.LogAsync("UpdateAppointment", "Appointment", appt.Id.ToString());

        var dto = ToDto(appt);
        var me = User.UserId();
        var other = appt.Patient.UserId == me ? appt.Doctor.UserId : appt.Patient.UserId;
        await hub.Clients.User(other.ToString()).SendAsync("appointmentUpdated", dto);
        await notify.SendAsync([other], NotificationType.Appointment,
            $"Appointment on {clock.ToLocal(appt.StartsAt):dd MMM, h:mm tt} ({appt.Doctor.User.FullName} / {appt.Patient.User.FullName}) is now {appt.Status}.");
        return dto;
    }
}
