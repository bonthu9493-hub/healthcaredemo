namespace HospitalApi.Data;

public static class Roles
{
    public const string Patient = "Patient";
    public const string Doctor = "Doctor";
    public const string Nurse = "Nurse";
    public const string Admin = "Admin";
}

public static class AppointmentStatus
{
    public const string Scheduled = "Scheduled";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
    public static readonly string[] All = [Scheduled, Completed, Cancelled, NoShow];
}

public static class Severity
{
    public const string Normal = "Normal";
    public const string Warning = "Warning";
    public const string Critical = "Critical";
}

public static class QueueStatus
{
    public const string Waiting = "Waiting";
    public const string Serving = "Serving";
    public const string Done = "Done";
    public const string Skipped = "Skipped";
}

public static class NotificationType
{
    public const string Appointment = "Appointment";
    public const string Vitals = "Vitals";
    public const string CodeBlue = "CodeBlue";
    public const string Queue = "Queue";
}

public class VitalSign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid RecordedByUserId { get; set; }
    public AppUser RecordedBy { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
    public int HeartRate { get; set; }
    public int Systolic { get; set; }
    public int Diastolic { get; set; }
    public int SpO2 { get; set; }
    public decimal TemperatureC { get; set; }
    public string Severity { get; set; } = HospitalApi.Data.Severity.Normal;
    public string? AlertReason { get; set; }
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}

public class QueueToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public int Number { get; set; }
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public string Status { get; set; } = QueueStatus.Waiting;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A weekly working window for a doctor, split into fixed-length bookable slots.</summary>
public class DoctorSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public DayOfWeek Day { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public int SlotMinutes { get; set; } = 30;
}

/// <summary>One shift for one staff member (doctor/nurse). At most one record per person per day.</summary>
public class AttendanceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public DateOnly Date { get; set; }
    public DateTimeOffset CheckIn { get; set; }
    public DateTimeOffset? CheckOut { get; set; }
}

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = Roles.Patient;
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Patient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public DateOnly DateOfBirth { get; set; }
    public string Phone { get; set; } = "";
}

public class Doctor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string Specialty { get; set; } = "";
}

public class Appointment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public DateTimeOffset StartsAt { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = AppointmentStatus.Scheduled;
}

public class MedicalRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public string Diagnosis { get; set; } = "";
    public string Prescription { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AuditLog
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string? EntityId { get; set; }
    public string? Ip { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
