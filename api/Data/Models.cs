namespace HospitalApi.Data;

public static class Roles
{
    public const string Patient = "Patient";
    public const string Doctor = "Doctor";
    public const string Nurse = "Nurse";
    public const string Pharmacist = "Pharmacist";
    public const string LabTech = "LabTech";
    public const string Admin = "Admin";

    /// <summary>Everyone who works shifts and so has attendance.</summary>
    public const string Staff = $"{Doctor},{Nurse},{Pharmacist},{LabTech}";
}

public static class AppointmentStatus
{
    public const string Scheduled = "Scheduled";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
    public static readonly string[] All = [Scheduled, Completed, Cancelled, NoShow];
}

public static class PrescriptionStatus
{
    public const string Pending = "Pending";
    public const string Dispensed = "Dispensed";
    public const string Cancelled = "Cancelled";
}

public static class LabStatus
{
    public const string Ordered = "Ordered";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

public static class InvoiceStatus
{
    public const string Open = "Open";
    public const string Paid = "Paid";
    public const string Void = "Void";
}

public static class InvoiceSource
{
    public const string Consultation = "Consultation";
    public const string Medicine = "Medicine";
    public const string Lab = "Lab";
    public const string Bed = "Bed";
    public const string Other = "Other";
}

public class Medicine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Strength { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Stock { get; set; }
    public int ReorderLevel { get; set; }
    public DateOnly ExpiryDate { get; set; }
}

public class Prescription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = PrescriptionStatus.Pending;
    public string Notes { get; set; } = "";
    public Guid? DispensedByUserId { get; set; }
    public DateTimeOffset? DispensedAt { get; set; }
    public List<PrescriptionItem> Items { get; set; } = [];
}

public class PrescriptionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PrescriptionId { get; set; }
    public Prescription Prescription { get; set; } = null!;
    public Guid MedicineId { get; set; }
    public Medicine Medicine { get; set; } = null!;
    public string Dosage { get; set; } = "";
    public int DurationDays { get; set; }
    public int Quantity { get; set; }
}

public class LabTest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public class LabOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public Guid LabTestId { get; set; }
    public LabTest LabTest { get; set; } = null!;
    public decimal Price { get; set; }
    public string Notes { get; set; } = "";
    public string Status { get; set; } = LabStatus.Ordered;
    public DateTimeOffset OrderedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ResultText { get; set; }
    public bool? Abnormal { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Number { get; set; } // assigned by the database (identity)
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = InvoiceStatus.Open;
    public DateTimeOffset? PaidAt { get; set; }
    public string? PaymentMethod { get; set; }
    public Guid? PaidByUserId { get; set; }
    public List<InvoiceItem> Items { get; set; } = [];
    public decimal Total => Items.Sum(i => i.Quantity * i.UnitPrice);
}

public class InvoiceItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public string Source { get; set; } = InvoiceSource.Other;
    public string Description { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
}

public class Ward
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Type { get; set; } = "General";
    public decimal DailyRate { get; set; }
    public List<Bed> Beds { get; set; } = [];
}

public class Bed
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WardId { get; set; }
    public Ward Ward { get; set; } = null!;
    public string Label { get; set; } = "";
}

public class Admission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid BedId { get; set; }
    public Bed Bed { get; set; } = null!;
    public Guid AdmittedByUserId { get; set; }
    public DateTimeOffset AdmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Reason { get; set; } = "";
    public DateTimeOffset? DischargedAt { get; set; }
    public Guid? DischargedByUserId { get; set; }
    public string? DischargeSummary { get; set; }
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
    public string? BloodGroup { get; set; }
    public string Allergies { get; set; } = "";
    public string ChronicConditions { get; set; } = "";
    public string EmergencyContactName { get; set; } = "";
    public string EmergencyContactPhone { get; set; } = "";
}

public class Doctor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string Specialty { get; set; } = "";
    public decimal ConsultationFee { get; set; } = 500m;
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
