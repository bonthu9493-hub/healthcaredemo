using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

/// <summary>Doctors and nurses record their own shift. Nobody can record attendance for someone else.</summary>
[ApiController, Route("api/attendance"), Authorize(Roles = $"{Roles.Doctor},{Roles.Nurse}")]
public class AttendanceController(AppDbContext db, AttendanceClock clock, AuditService audit) : ControllerBase
{
    private AttendanceDto ToDto(AttendanceRecord r) => new(r.Id, r.Date, r.CheckIn, r.CheckOut,
        r.CheckOut is null ? null : Math.Round((r.CheckOut.Value - r.CheckIn).TotalHours, 2), clock.IsLate(r.CheckIn));

    [HttpGet("me")]
    public async Task<AttendanceSummary> Me()
    {
        var uid = User.UserId();
        var today = clock.Today();
        var since = today.AddDays(-45);
        var rows = await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.UserId == uid && r.Date >= since).OrderByDescending(r => r.Date).ToListAsync();
        var current = rows.FirstOrDefault(r => r.Date == today || r.CheckOut == null);
        return new AttendanceSummary(current is null ? null : ToDto(current), rows.Select(ToDto).ToList());
    }

    [HttpPost("check-in")]
    public async Task<ActionResult<AttendanceDto>> CheckIn()
    {
        var uid = User.UserId();
        var today = clock.Today();
        if (await db.AttendanceRecords.AnyAsync(r => r.UserId == uid && (r.Date == today || r.CheckOut == null)))
            return Conflict(new { error = "You are already checked in, or have already completed today's shift." });

        var rec = new AttendanceRecord { UserId = uid, Date = today, CheckIn = DateTimeOffset.UtcNow };
        db.AttendanceRecords.Add(rec);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { error = "You are already checked in today." }); }
        await audit.LogAsync("CheckIn", "Attendance", rec.Id.ToString());
        return StatusCode(201, ToDto(rec));
    }

    [HttpPost("check-out")]
    public async Task<ActionResult<AttendanceDto>> CheckOut()
    {
        var uid = User.UserId();
        // Latest open shift, so a shift that runs past midnight can still be closed.
        var rec = await db.AttendanceRecords.Where(r => r.UserId == uid && r.CheckOut == null)
            .OrderByDescending(r => r.CheckIn).FirstOrDefaultAsync();
        if (rec is null) return Conflict(new { error = "You are not checked in." });

        rec.CheckOut = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync("CheckOut", "Attendance", rec.Id.ToString());
        return ToDto(rec);
    }
}
