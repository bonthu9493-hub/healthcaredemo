using System.ComponentModel.DataAnnotations;

namespace HospitalApi.Dtos;

/// <summary>Rejects markup characters so stored text can never carry HTML/script payloads.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class NoHtmlAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext ctx) =>
        value is string s && s.IndexOfAny(['<', '>']) >= 0
            ? new ValidationResult($"{ctx.DisplayName} contains invalid characters.")
            : ValidationResult.Success;
}

public class RegisterRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    // 10+ chars with upper, lower and a digit
    [Required, StringLength(128, MinimumLength = 10),
     RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$", ErrorMessage = "Password needs upper, lower case and a digit.")]
    public string Password { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 2), NoHtml] public string FullName { get; set; } = "";
    [Required, RegularExpression(@"^\+?[0-9 \-]{7,20}$", ErrorMessage = "Invalid phone number.")] public string Phone { get; set; } = "";
    [Required] public DateOnly DateOfBirth { get; set; }
}

public class LoginRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128)] public string Password { get; set; } = "";
}

public record AuthResponse(string Token, DateTimeOffset ExpiresAt, string Role, string FullName);

public class CreateDoctorRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 10),
     RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$", ErrorMessage = "Password needs upper, lower case and a digit.")]
    public string Password { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 2), NoHtml] public string FullName { get; set; } = "";
    [Required, StringLength(80, MinimumLength = 2), NoHtml] public string Specialty { get; set; } = "";
}

public class CreateNurseRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 10),
     RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$", ErrorMessage = "Password needs upper, lower case and a digit.")]
    public string Password { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 2), NoHtml] public string FullName { get; set; } = "";
}

public record DoctorDto(Guid Id, string FullName, string Specialty);
public record UserDto(Guid Id, string Email, string FullName, string Role);

public class CreateAppointmentRequest
{
    [Required] public Guid DoctorId { get; set; }
    [Required] public DateTimeOffset StartsAt { get; set; }
    [Required, StringLength(500, MinimumLength = 3), NoHtml] public string Reason { get; set; } = "";
}

public class UpdateStatusRequest
{
    [Required, StringLength(20)] public string Status { get; set; } = "";
}

public record AppointmentDto(Guid Id, Guid PatientId, string PatientName, Guid DoctorId, string DoctorName,
    string Specialty, DateTimeOffset StartsAt, string Reason, string Status);

public class CreateRecordRequest
{
    [Required] public Guid PatientId { get; set; }
    [Required, StringLength(1000, MinimumLength = 2), NoHtml] public string Diagnosis { get; set; } = "";
    [StringLength(1000), NoHtml] public string Prescription { get; set; } = "";
    [StringLength(4000), NoHtml] public string Notes { get; set; } = "";
}

public record RecordDto(Guid Id, Guid PatientId, string PatientName, string DoctorName, string Diagnosis,
    string Prescription, string Notes, DateTimeOffset CreatedAt);

public record AttendanceDto(Guid Id, DateOnly Date, DateTimeOffset CheckIn, DateTimeOffset? CheckOut, double? Hours, bool Late);
public record AttendanceSummary(AttendanceDto? Today, List<AttendanceDto> History);
public record StaffReportRow(Guid UserId, string FullName, string Role, int WorkingDays, int DaysPresent,
    int DaysAbsent, double TotalHours, int LateCount, int MissingCheckOut);
public record AppointmentReportRow(Guid DoctorId, string DoctorName, string Specialty, int Total, int Attended,
    int NoShow, int Cancelled, int Upcoming, int Unmarked, double? AttendanceRate);

public record PatientDto(Guid Id, string FullName, DateOnly DateOfBirth, string Phone);
public record AuditDto(long Id, Guid? UserId, string Action, string Entity, string? EntityId, string? Ip, DateTimeOffset At);
