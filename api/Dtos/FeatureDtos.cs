using System.ComponentModel.DataAnnotations;

namespace HospitalApi.Dtos;

// ---- Vitals ----
public class CreateVitalsRequest
{
    [Required] public Guid PatientId { get; set; }
    [Range(20, 250)] public int HeartRate { get; set; }
    [Range(50, 260)] public int Systolic { get; set; }
    [Range(30, 160)] public int Diastolic { get; set; }
    [Range(50, 100)] public int SpO2 { get; set; }
    [Range(30.0, 45.0)] public decimal TemperatureC { get; set; }
}

public record VitalsDto(Guid Id, Guid PatientId, string PatientName, string RecordedByName, DateTimeOffset RecordedAt,
    int HeartRate, int Systolic, int Diastolic, int SpO2, decimal TemperatureC, string Severity, string? AlertReason);

// ---- Notifications ----
public record NotificationDto(Guid Id, string Type, string Message, DateTimeOffset CreatedAt, bool Read);
public record NotificationList(List<NotificationDto> Items, int Unread);

// ---- Emergency ----
public class CodeBlueRequest
{
    [Required, StringLength(100, MinimumLength = 2), NoHtml] public string Location { get; set; } = "";
}

// ---- Queue ----
public class IssueTokenRequest
{
    [Required] public Guid PatientId { get; set; }
    [Required] public Guid DoctorId { get; set; }
}

public record QueueEntryDto(Guid Id, int Number, string? PatientName, bool Mine);
public record BoardDoctorDto(Guid DoctorId, string DoctorName, string Specialty, int? NowServing, List<QueueEntryDto> Waiting);

// ---- Schedules ----
public class ScheduleEntryRequest
{
    [Range(0, 6)] public int Day { get; set; }
    [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")] public string Start { get; set; } = "";
    [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")] public string End { get; set; } = "";
    [Range(10, 120)] public int SlotMinutes { get; set; } = 30;
}

public record ScheduleEntryDto(int Day, string Start, string End, int SlotMinutes);
public record SlotsResponse(bool HasSchedule, List<DateTimeOffset> Slots);

// ---- Patient lookup (name only) ----
public record PatientLookupDto(Guid Id, string FullName);
