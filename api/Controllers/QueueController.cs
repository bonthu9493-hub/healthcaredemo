using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Hubs;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api/queue"), Authorize]
public class QueueController(AppDbContext db, AttendanceClock clock, NotificationService notify,
    IHubContext<NotificationsHub> hub, AuditService audit) : ControllerBase
{
    // The signal carries no data; every client re-fetches the board through the authorised endpoint.
    private Task Broadcast() => hub.Clients.All.SendAsync("queueChanged");

    /// <summary>Today's queue per doctor. Staff see patient names; a patient sees only their own name.</summary>
    [HttpGet("board")]
    public async Task<List<BoardDoctorDto>> Board()
    {
        var today = clock.Today();
        var me = User.UserId();
        var staff = User.RoleName() != Roles.Patient;

        var doctors = await db.Doctors.AsNoTracking().OrderBy(d => d.User.FullName)
            .Select(d => new { d.Id, Name = d.User.FullName, d.Specialty }).ToListAsync();
        var tokens = await db.QueueTokens.AsNoTracking()
            .Where(t => t.Date == today && (t.Status == QueueStatus.Waiting || t.Status == QueueStatus.Serving))
            .Select(t => new { t.Id, t.DoctorId, t.Number, t.Status, Name = t.Patient.User.FullName, PatientUserId = t.Patient.UserId })
            .ToListAsync();

        return doctors.Select(d =>
        {
            var mine = tokens.Where(t => t.DoctorId == d.Id).OrderBy(t => t.Number).ToList();
            return new BoardDoctorDto(d.Id, d.Name, d.Specialty,
                mine.FirstOrDefault(t => t.Status == QueueStatus.Serving)?.Number,
                mine.Where(t => t.Status == QueueStatus.Waiting)
                    .Select(t => new QueueEntryDto(t.Id, t.Number, staff || t.PatientUserId == me ? t.Name : null, t.PatientUserId == me))
                    .ToList());
        }).ToList();
    }

    [HttpPost("tokens"), Authorize(Roles = $"{Roles.Nurse},{Roles.Admin}")]
    public async Task<ActionResult<QueueEntryDto>> Issue(IssueTokenRequest req)
    {
        var patient = await db.Patients.Include(p => p.User).FirstOrDefaultAsync(p => p.Id == req.PatientId);
        var doctor = await db.Doctors.Include(d => d.User).FirstOrDefaultAsync(d => d.Id == req.DoctorId);
        if (patient is null || doctor is null) return NotFound();

        var today = clock.Today();
        if (await db.QueueTokens.AnyAsync(t => t.Date == today && t.PatientId == patient.Id && t.DoctorId == doctor.Id
                && (t.Status == QueueStatus.Waiting || t.Status == QueueStatus.Serving)))
            return Conflict(new { error = "This patient already has an active token for that doctor." });

        QueueToken? token = null;
        for (var attempt = 0; attempt < 3 && token is null; attempt++)
        {
            var next = (await db.QueueTokens.Where(t => t.Date == today && t.DoctorId == doctor.Id)
                .MaxAsync(t => (int?)t.Number) ?? 0) + 1;
            var candidate = new QueueToken { Date = today, Number = next, PatientId = patient.Id, DoctorId = doctor.Id };
            db.QueueTokens.Add(candidate);
            try { await db.SaveChangesAsync(); token = candidate; }
            catch (DbUpdateException) { db.Entry(candidate).State = EntityState.Detached; } // number taken: retry
        }
        if (token is null) return Conflict(new { error = "Could not issue a token. Please try again." });

        await audit.LogAsync("IssueToken", "QueueToken", token.Id.ToString());
        await notify.SendAsync([patient.UserId], NotificationType.Queue,
            $"Your token is #{token.Number} for {doctor.User.FullName}.");
        await Broadcast();
        return StatusCode(201, new QueueEntryDto(token.Id, token.Number, patient.User.FullName, false));
    }

    /// <summary>The doctor finishes the current patient and calls the next one in line.</summary>
    [HttpPost("call-next"), Authorize(Roles = Roles.Doctor)]
    public async Task<IActionResult> CallNext()
    {
        var uid = User.UserId();
        var doctor = await db.Doctors.Include(d => d.User).FirstAsync(d => d.UserId == uid);
        var today = clock.Today();

        var active = await db.QueueTokens.Include(t => t.Patient)
            .Where(t => t.Date == today && t.DoctorId == doctor.Id
                && (t.Status == QueueStatus.Waiting || t.Status == QueueStatus.Serving))
            .OrderBy(t => t.Number).ToListAsync();

        foreach (var t in active.Where(t => t.Status == QueueStatus.Serving)) t.Status = QueueStatus.Done;
        var next = active.FirstOrDefault(t => t.Status == QueueStatus.Waiting);
        if (next is not null) next.Status = QueueStatus.Serving;
        await db.SaveChangesAsync();

        if (next is not null)
        {
            await audit.LogAsync("CallNext", "QueueToken", next.Id.ToString());
            await notify.SendAsync([next.Patient.UserId], NotificationType.Queue,
                $"Token #{next.Number}: {doctor.User.FullName} is ready to see you now.");
        }
        await Broadcast();
        return Ok(new { nowServing = next?.Number });
    }

    [HttpPost("tokens/{id:guid}/skip"), Authorize(Roles = $"{Roles.Doctor},{Roles.Nurse},{Roles.Admin}")]
    public async Task<IActionResult> Skip(Guid id)
    {
        var uid = User.UserId();
        var today = clock.Today();
        var token = await db.QueueTokens.Include(t => t.Doctor)
            .FirstOrDefaultAsync(t => t.Id == id && t.Date == today
                && (t.Status == QueueStatus.Waiting || t.Status == QueueStatus.Serving));
        // A doctor can only manage their own queue.
        if (token is null || (User.RoleName() == Roles.Doctor && token.Doctor.UserId != uid)) return NotFound();

        token.Status = QueueStatus.Skipped;
        await db.SaveChangesAsync();
        await audit.LogAsync("SkipToken", "QueueToken", token.Id.ToString());
        await Broadcast();
        return NoContent();
    }
}
