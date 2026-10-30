namespace HospitalApi.Services;

/// <summary>Hospital-local time rules for attendance (time zone, shift start, grace period).</summary>
public class AttendanceClock
{
    public TimeZoneInfo Zone { get; }
    public TimeOnly ShiftStart { get; }
    public int GraceMinutes { get; }

    public AttendanceClock(IConfiguration cfg)
    {
        try { Zone = TimeZoneInfo.FindSystemTimeZoneById(cfg["Attendance:TimeZone"] ?? "Asia/Kolkata"); }
        catch (TimeZoneNotFoundException) { Zone = TimeZoneInfo.Local; }
        ShiftStart = TimeOnly.TryParse(cfg["Attendance:ShiftStart"], out var s) ? s : new TimeOnly(9, 0);
        GraceMinutes = int.TryParse(cfg["Attendance:GraceMinutes"], out var g) ? g : 15;
    }

    public DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    public DateOnly Today() => DateOnly.FromDateTime(ToLocal(DateTimeOffset.UtcNow).DateTime);

    public bool IsLate(DateTimeOffset checkIn) =>
        TimeOnly.FromDateTime(ToLocal(checkIn).DateTime) > ShiftStart.AddMinutes(GraceMinutes);

    /// <summary>Midnight at the start of this local date, as a UTC instant.</summary>
    public DateTimeOffset StartOfDayUtc(DateOnly d) =>
        new(TimeZoneInfo.ConvertTimeToUtc(d.ToDateTime(TimeOnly.MinValue), Zone), TimeSpan.Zero);
}
