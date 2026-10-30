using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api/doctors"), Authorize]
public class DoctorsController(AppDbContext db, AttendanceClock clock) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<DoctorDto>> List() =>
        await db.Doctors.AsNoTracking().OrderBy(d => d.User.FullName)
            .Select(d => new DoctorDto(d.Id, d.User.FullName, d.Specialty)).ToListAsync();

    /// <summary>Free, bookable slots for one doctor on one day. If the doctor has no schedule yet, any time is allowed.</summary>
    [HttpGet("{id:guid}/slots")]
    public async Task<ActionResult<SlotsResponse>> Slots(Guid id, DateOnly date)
    {
        var today = clock.Today();
        if (date < today || date > today.AddDays(60))
            return BadRequest(new { error = "Choose a date within the next 60 days." });
        if (!await db.Doctors.AnyAsync(d => d.Id == id)) return NotFound();

        var all = await db.DoctorSchedules.AsNoTracking().Where(s => s.DoctorId == id).ToListAsync();
        if (all.Count == 0) return new SlotsResponse(false, []);

        var dayStart = clock.StartOfDayUtc(date);
        var dayEnd = clock.StartOfDayUtc(date.AddDays(1));
        var booked = await db.Appointments.AsNoTracking()
            .Where(a => a.DoctorId == id && a.Status == AppointmentStatus.Scheduled
                && a.StartsAt >= dayStart && a.StartsAt < dayEnd)
            .Select(a => a.StartsAt).ToListAsync();

        var now = DateTimeOffset.UtcNow;
        var slots = new List<DateTimeOffset>();
        foreach (var e in all.Where(s => s.Day == date.DayOfWeek).OrderBy(s => s.Start))
            for (var t = e.Start; t.AddMinutes(e.SlotMinutes) <= e.End; t = t.AddMinutes(e.SlotMinutes))
            {
                var utc = clock.LocalToUtc(date, t);
                var end = utc.AddMinutes(e.SlotMinutes);
                if (utc > now && !booked.Any(b => b >= utc && b < end)) slots.Add(utc);
            }
        return new SlotsResponse(true, slots);
    }
}
