using System.ComponentModel.DataAnnotations;

namespace HospitalApi.Dtos;

// ---- Staff accounts ----
public class CreateStaffRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 10),
     RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$", ErrorMessage = "Password needs upper, lower case and a digit.")]
    public string Password { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 2), NoHtml] public string FullName { get; set; } = "";
    [Required, RegularExpression("^(Nurse|Pharmacist|LabTech)$", ErrorMessage = "Role must be Nurse, Pharmacist or LabTech.")]
    public string Role { get; set; } = "";
}

// ---- Patient profile ----
public class UpdateProfileRequest
{
    [Required, RegularExpression(@"^\+?[0-9 \-]{7,20}$", ErrorMessage = "Invalid phone number.")] public string Phone { get; set; } = "";
    [RegularExpression(@"^(A|B|AB|O)[+-]$", ErrorMessage = "Blood group must be like A+, O-, AB+.")] public string? BloodGroup { get; set; }
    [StringLength(500), NoHtml] public string Allergies { get; set; } = "";
    [StringLength(500), NoHtml] public string ChronicConditions { get; set; } = "";
    [StringLength(100), NoHtml] public string EmergencyContactName { get; set; } = "";
    [RegularExpression(@"^(\+?[0-9 \-]{7,20})?$", ErrorMessage = "Invalid phone number.")] public string EmergencyContactPhone { get; set; } = "";
}

public record PatientProfileDto(Guid Id, string FullName, DateOnly DateOfBirth, string Phone, string? BloodGroup,
    string Allergies, string ChronicConditions, string EmergencyContactName, string EmergencyContactPhone);

/// <summary>What clinical staff need at the bedside: no phone number, no date of birth.</summary>
public record PatientSummaryDto(Guid Id, string FullName, int Age, string? BloodGroup, string Allergies, string ChronicConditions);

// ---- Pharmacy ----
public class CreateMedicineRequest
{
    [Required, StringLength(100, MinimumLength = 2), NoHtml] public string Name { get; set; } = "";
    [StringLength(50), NoHtml] public string Strength { get; set; } = "";
    [Range(0.01, 100000)] public decimal UnitPrice { get; set; }
    [Range(0, 1000000)] public int Stock { get; set; }
    [Range(0, 100000)] public int ReorderLevel { get; set; }
    public DateOnly ExpiryDate { get; set; }
}

public class RestockRequest
{
    [Range(1, 100000)] public int Quantity { get; set; }
    public DateOnly? NewExpiryDate { get; set; }
}

public record MedicineDto(Guid Id, string Name, string Strength, decimal UnitPrice, int Stock, int ReorderLevel,
    DateOnly ExpiryDate, bool LowStock, bool Expired, bool ExpiringSoon);

public class PrescriptionItemRequest
{
    [Required] public Guid MedicineId { get; set; }
    [Required, StringLength(200, MinimumLength = 2), NoHtml] public string Dosage { get; set; } = "";
    [Range(1, 365)] public int DurationDays { get; set; }
    [Range(1, 1000)] public int Quantity { get; set; }
}

public class CreatePrescriptionRequest
{
    [Required] public Guid PatientId { get; set; }
    [Required, MinLength(1), MaxLength(10)] public List<PrescriptionItemRequest> Items { get; set; } = [];
    [StringLength(500), NoHtml] public string Notes { get; set; } = "";
    /// <summary>The doctor has seen the allergy warning and still wants to prescribe.</summary>
    public bool OverrideAllergyWarning { get; set; }
}

public record PrescriptionItemDto(Guid MedicineId, string MedicineName, string Strength, string Dosage, int DurationDays, int Quantity);
public record PrescriptionDto(Guid Id, Guid PatientId, string PatientName, string DoctorName, DateTimeOffset CreatedAt,
    string Status, string Notes, List<PrescriptionItemDto> Items);

// ---- Lab ----
public class CreateLabTestRequest
{
    [Required, StringLength(100, MinimumLength = 2), NoHtml] public string Name { get; set; } = "";
    [Range(0.01, 100000)] public decimal Price { get; set; }
}

public class OrderLabRequest
{
    [Required] public Guid PatientId { get; set; }
    [Required] public Guid LabTestId { get; set; }
    [StringLength(500), NoHtml] public string Notes { get; set; } = "";
}

public class LabResultRequest
{
    [Required, StringLength(2000, MinimumLength = 2), NoHtml] public string ResultText { get; set; } = "";
    public bool Abnormal { get; set; }
}

public record LabTestDto(Guid Id, string Name, decimal Price);
public record LabOrderDto(Guid Id, Guid PatientId, string PatientName, string DoctorName, string TestName, decimal Price,
    string Notes, string Status, DateTimeOffset OrderedAt, string? ResultText, bool? Abnormal, DateTimeOffset? CompletedAt);

// ---- Billing ----
public class AddInvoiceItemRequest
{
    [Required, StringLength(200, MinimumLength = 2), NoHtml] public string Description { get; set; } = "";
    [Range(1, 1000)] public int Quantity { get; set; } = 1;
    [Range(0.01, 1000000)] public decimal UnitPrice { get; set; }
}

public class PayInvoiceRequest
{
    [Required, RegularExpression("^(Cash|Card|UPI)$", ErrorMessage = "Method must be Cash, Card or UPI.")]
    public string Method { get; set; } = "";
}

public record InvoiceItemDto(Guid Id, string Source, string Description, int Quantity, decimal UnitPrice, decimal Total);
public record InvoiceDto(Guid Id, string Number, Guid PatientId, string PatientName, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt, string? PaymentMethod, decimal Total, List<InvoiceItemDto> Items);
public record BillingSourceRow(string Source, decimal Revenue);
public record BillingReport(int PaidInvoices, int OpenInvoices, decimal Revenue, decimal Outstanding, List<BillingSourceRow> BySource);

// ---- Wards and beds ----
public class CreateWardRequest
{
    [Required, StringLength(60, MinimumLength = 2), NoHtml] public string Name { get; set; } = "";
    [Required, RegularExpression("^(General|ICU|Private|Maternity|Emergency)$")] public string Type { get; set; } = "General";
    [Range(0, 1000000)] public decimal DailyRate { get; set; }
}

public class CreateBedRequest
{
    [Required, StringLength(30, MinimumLength = 1), NoHtml] public string Label { get; set; } = "";
}

public class AdmitRequest
{
    [Required] public Guid PatientId { get; set; }
    [Required] public Guid BedId { get; set; }
    [Required, StringLength(300, MinimumLength = 3), NoHtml] public string Reason { get; set; } = "";
}

public class DischargeRequest
{
    [Required, StringLength(2000, MinimumLength = 5), NoHtml] public string Summary { get; set; } = "";
}

public record BedDto(Guid Id, string Label, Guid? AdmissionId, Guid? PatientId, string? PatientName, DateTimeOffset? AdmittedAt);
public record WardDto(Guid Id, string Name, string Type, decimal DailyRate, int TotalBeds, int Occupied, List<BedDto> Beds);
public record AdmissionDto(Guid Id, string WardName, string BedLabel, string Reason, DateTimeOffset AdmittedAt,
    DateTimeOffset? DischargedAt, string? DischargeSummary);
