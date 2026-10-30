using System.Globalization;
using System.Text;
using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api/admin/reports"), Authorize(Roles = Roles.Admin)]
public class ReportsController(AppDbContext db, AttendanceClock clock, AuditService audit) : ControllerBase
{
    private const int MaxDays = 366;

    private bool TryRange(DateOnly? from, DateOnly? to, out DateOnly f, out DateOnly t)
    {
        t = to ?? clock.Today();
        f = from ?? t.AddDays(-29);
        return f <= t && t.DayNumber - f.DayNumber < MaxDays;
    }

    // ---------- Staff attendance ----------

    private async Task<List<StaffReportRow>> StaffRows(DateOnly f, DateOnly t)
    {
        var staff = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Doctor || u.Role == Roles.Nurse || u.Role == Roles.Pharmacist || u.Role == Roles.LabTech)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Role, u.CreatedAt }).ToListAsync();
        var records = (await db.AttendanceRecords.AsNoTracking()
            .Where(r => r.Date >= f && r.Date <= t).ToListAsync()).ToLookup(r => r.UserId);

        var last = t < clock.Today() ? t : clock.Today(); // future days are not "absent"
        var rows = new List<StaffReportRow>();
        foreach (var s in staff)
        {
            var joined = DateOnly.FromDateTime(clock.ToLocal(s.CreatedAt).DateTime);
            var start = f > joined ? f : joined;           // no absences before the account existed
            var mine = records[s.Id].ToList();
            var presentDates = mine.Select(r => r.Date).ToHashSet();

            int working = 0, absent = 0;
            for (var d = start; d <= last; d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                working++;
                if (!presentDates.Contains(d)) absent++;
            }

            var hours = mine.Where(r => r.CheckOut != null).Sum(r => (r.CheckOut!.Value - r.CheckIn).TotalHours);
            rows.Add(new StaffReportRow(s.Id, s.FullName, s.Role, working, presentDates.Count, absent,
                Math.Round(hours, 2), mine.Count(r => clock.IsLate(r.CheckIn)), mine.Count(r => r.CheckOut == null)));
        }
        return rows;
    }

    [HttpGet("staff-attendance")]
    public async Task<ActionResult<List<StaffReportRow>>> Staff(DateOnly? from, DateOnly? to)
    {
        if (!TryRange(from, to, out var f, out var t)) return BadRequest(new { error = "Invalid date range (max 366 days)." });
        return await StaffRows(f, t);
    }

    [HttpGet("staff-attendance.csv")]
    public async Task<IActionResult> StaffCsv(DateOnly? from, DateOnly? to)
    {
        if (!TryRange(from, to, out var f, out var t)) return BadRequest(new { error = "Invalid date range (max 366 days)." });
        var rows = await StaffRows(f, t);
        await audit.LogAsync("ExportReport", "StaffAttendance");
        return Csv($"staff-attendance_{f:yyyy-MM-dd}_to_{t:yyyy-MM-dd}.csv",
            ["Name", "Role", "Working days", "Days present", "Days absent", "Total hours", "Late arrivals", "Missing check-out"],
            rows.Select(r => new[] { r.FullName, r.Role, r.WorkingDays.ToString(), r.DaysPresent.ToString(),
                r.DaysAbsent.ToString(), r.TotalHours.ToString("0.##", CultureInfo.InvariantCulture),
                r.LateCount.ToString(), r.MissingCheckOut.ToString() }));
    }

    // ---------- Appointment attendance (attended vs no-show) ----------

    private async Task<List<AppointmentReportRow>> AppointmentRows(DateOnly f, DateOnly t)
    {
        var startUtc = clock.StartOfDayUtc(f);
        var endUtc = clock.StartOfDayUtc(t.AddDays(1));
        var now = DateTimeOffset.UtcNow;
        var items = await db.Appointments.AsNoTracking()
            .Where(a => a.StartsAt >= startUtc && a.StartsAt < endUtc)
            .Select(a => new { a.DoctorId, Name = a.Doctor.User.FullName, a.Doctor.Specialty, a.Status, a.StartsAt })
            .ToListAsync();

        return items.GroupBy(a => new { a.DoctorId, a.Name, a.Specialty }).Select(g =>
        {
            int attended = g.Count(a => a.Status == AppointmentStatus.Completed);
            int noShow = g.Count(a => a.Status == AppointmentStatus.NoShow);
            return new AppointmentReportRow(g.Key.DoctorId, g.Key.Name, g.Key.Specialty, g.Count(), attended, noShow,
                g.Count(a => a.Status == AppointmentStatus.Cancelled),
                g.Count(a => a.Status == AppointmentStatus.Scheduled && a.StartsAt > now),
                g.Count(a => a.Status == AppointmentStatus.Scheduled && a.StartsAt <= now),
                attended + noShow == 0 ? null : Math.Round(100.0 * attended / (attended + noShow), 1));
        }).OrderBy(r => r.DoctorName).ToList();
    }

    [HttpGet("appointment-attendance")]
    public async Task<ActionResult<List<AppointmentReportRow>>> Appointments(DateOnly? from, DateOnly? to)
    {
        if (!TryRange(from, to, out var f, out var t)) return BadRequest(new { error = "Invalid date range (max 366 days)." });
        return await AppointmentRows(f, t);
    }

    [HttpGet("appointment-attendance.csv")]
    public async Task<IActionResult> AppointmentsCsv(DateOnly? from, DateOnly? to)
    {
        if (!TryRange(from, to, out var f, out var t)) return BadRequest(new { error = "Invalid date range (max 366 days)." });
        var rows = await AppointmentRows(f, t);
        await audit.LogAsync("ExportReport", "AppointmentAttendance");
        return Csv($"appointment-attendance_{f:yyyy-MM-dd}_to_{t:yyyy-MM-dd}.csv",
            ["Doctor", "Specialty", "Total", "Attended", "No-show", "Cancelled", "Upcoming", "Not marked (past)", "Attendance rate %"],
            rows.Select(r => new[] { r.DoctorName, r.Specialty, r.Total.ToString(), r.Attended.ToString(),
                r.NoShow.ToString(), r.Cancelled.ToString(), r.Upcoming.ToString(), r.Unmarked.ToString(),
                r.AttendanceRate?.ToString("0.#", CultureInfo.InvariantCulture) ?? "" }));
    }

    // ---------- Billing summary ----------

    /// <summary>Money collected (paid invoices), money still owed (open invoices), and revenue by source.</summary>
    [HttpGet("billing")]
    public async Task<ActionResult<BillingReport>> Billing(DateOnly? from, DateOnly? to)
    {
        if (!TryRange(from, to, out var f, out var t)) return BadRequest(new { error = "Invalid date range (max 366 days)." });
        var startUtc = clock.StartOfDayUtc(f);
        var endUtc = clock.StartOfDayUtc(t.AddDays(1));

        var invoices = await db.Invoices.AsNoTracking().Include(i => i.Items)
            .Where(i => i.CreatedAt >= startUtc && i.CreatedAt < endUtc && i.Status != InvoiceStatus.Void).ToListAsync();
        var paid = invoices.Where(i => i.Status == InvoiceStatus.Paid).ToList();
        var open = invoices.Where(i => i.Status == InvoiceStatus.Open).ToList();
        var bySource = paid.SelectMany(i => i.Items).GroupBy(x => x.Source)
            .Select(g => new BillingSourceRow(g.Key, g.Sum(x => x.Quantity * x.UnitPrice))).OrderByDescending(r => r.Revenue).ToList();
        return new BillingReport(paid.Count, open.Count, paid.Sum(i => i.Total), open.Sum(i => i.Total), bySource);
    }

    // ---------- CSV helper ----------

    private FileContentResult Csv(string fileName, string[] header, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', header.Select(Cell)));
        foreach (var r in rows) sb.AppendLine(string.Join(',', r.Select(Cell)));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", fileName);
    }

    /// <summary>Quotes a cell, and neutralises spreadsheet formulas (=, +, -, @) so opened CSVs can't run code.</summary>
    private static string Cell(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
