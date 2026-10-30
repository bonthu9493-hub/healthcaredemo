using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api/schedule"), Authorize(Roles = Roles.Doctor)]
public class ScheduleController(AppDbContext db, AuditService audit) : ControllerBase
{
    private static ScheduleEntryDto ToDto(DoctorSchedule s) =>
        new((int)s.Day, s.Start.ToString("HH:mm"), s.End.ToString("HH:mm"), s.SlotMinutes);

    [HttpGet("me")]
    public async Task<List<ScheduleEntryDto>> Mine()
    {
        var uid = User.UserId();
        var rows = await db.DoctorSchedules.AsNoTracking().Where(s => s.Doctor.UserId == uid).ToListAsync();
        return rows.OrderBy(s => s.Day).ThenBy(s => s.Start).Select(ToDto).ToList();
    }

    /// <summary>Replaces the doctor's whole weekly schedule.</summary>
    [HttpPut("me")]
    public async Task<ActionResult<List<ScheduleEntryDto>>> Replace(List<ScheduleEntryRequest> req)
    {
        if (req.Count > 21) return BadRequest(new { error = "Too many schedule entries." });

        var parsed = new List<DoctorSchedule>();
        foreach (var e in req)
        {
            var start = TimeOnly.ParseExact(e.Start, "HH:mm");
            var end = TimeOnly.ParseExact(e.End, "HH:mm");
            if (end <= start.AddMinutes(e.SlotMinutes - 1))
                return BadRequest(new { error = "Each window must end after it starts and fit at least one slot." });
            parsed.Add(new DoctorSchedule { Day = (DayOfWeek)e.Day, Start = start, End = end, SlotMinutes = e.SlotMinutes });
        }
        foreach (var day in parsed.GroupBy(p => p.Day))
        {
            var list = day.OrderBy(p => p.Start).ToList();
            for (var i = 1; i < list.Count; i++)
                if (list[i].Start < list[i - 1].End)
                    return BadRequest(new { error = "Time windows on the same day can't overlap." });
        }

        var uid = User.UserId();
        var doctor = await db.Doctors.FirstAsync(d => d.UserId == uid);
        var old = await db.DoctorSchedules.Where(s => s.DoctorId == doctor.Id).ToListAsync();
        db.DoctorSchedules.RemoveRange(old);
        foreach (var p in parsed) p.DoctorId = doctor.Id;
        db.DoctorSchedules.AddRange(parsed);
        await db.SaveChangesAsync();
        await audit.LogAsync("UpdateSchedule", "Doctor", doctor.Id.ToString());
        return parsed.OrderBy(s => s.Day).ThenBy(s => s.Start).Select(ToDto).ToList();
    }
}
