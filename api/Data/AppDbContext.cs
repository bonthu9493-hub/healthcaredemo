using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<VitalSign> VitalSigns => Set<VitalSign>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<QueueToken> QueueTokens => Set<QueueToken>();
    public DbSet<DoctorSchedule> DoctorSchedules => Set<DoctorSchedule>();
    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
    public DbSet<LabTest> LabTests => Set<LabTest>();
    public DbSet<LabOrder> LabOrders => Set<LabOrder>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Ward> Wards => Set<Ward>();
    public DbSet<Bed> Beds => Set<Bed>();
    public DbSet<Admission> Admissions => Set<Admission>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.FullName).HasMaxLength(100);
            e.Property(x => x.Role).HasMaxLength(20);
            e.Property(x => x.PasswordHash).HasMaxLength(100);
        });
        b.Entity<Patient>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.Phone).HasMaxLength(20);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Doctor>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.Specialty).HasMaxLength(80);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Appointment>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => new { x.DoctorId, x.StartsAt });
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<MedicalRecord>(e =>
        {
            e.Property(x => x.Diagnosis).HasMaxLength(1000);
            e.Property(x => x.Prescription).HasMaxLength(1000);
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Patient>(e =>
        {
            e.Property(x => x.BloodGroup).HasMaxLength(3);
            e.Property(x => x.Allergies).HasMaxLength(500);
            e.Property(x => x.ChronicConditions).HasMaxLength(500);
            e.Property(x => x.EmergencyContactName).HasMaxLength(100);
            e.Property(x => x.EmergencyContactPhone).HasMaxLength(20);
        });
        b.Entity<Doctor>().Property(x => x.ConsultationFee).HasPrecision(10, 2);

        b.Entity<Medicine>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Strength).HasMaxLength(50);
            e.Property(x => x.UnitPrice).HasPrecision(10, 2);
        });
        b.Entity<Prescription>(e =>
        {
            e.HasIndex(x => new { x.PatientId, x.CreatedAt });
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PrescriptionItem>(e =>
        {
            e.Property(x => x.Dosage).HasMaxLength(200);
            e.HasOne(x => x.Prescription).WithMany(p => p.Items).HasForeignKey(x => x.PrescriptionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Medicine).WithMany().HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LabTest>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Price).HasPrecision(10, 2);
        });
        b.Entity<LabOrder>(e =>
        {
            e.HasIndex(x => new { x.PatientId, x.OrderedAt });
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.ResultText).HasMaxLength(2000);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.LabTest).WithMany().HasForeignKey(x => x.LabTestId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Invoice>(e =>
        {
            e.Property(x => x.Number).UseIdentityColumn();
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.PatientId, x.Status });
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.PaymentMethod).HasMaxLength(10);
            e.Ignore(x => x.Total);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<InvoiceItem>(e =>
        {
            e.Property(x => x.Source).HasMaxLength(15);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.UnitPrice).HasPrecision(10, 2);
            e.HasOne(x => x.Invoice).WithMany(i => i.Items).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Ward>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Type).HasMaxLength(30);
            e.Property(x => x.DailyRate).HasPrecision(10, 2);
        });
        b.Entity<Bed>(e =>
        {
            e.HasIndex(x => new { x.WardId, x.Label }).IsUnique();
            e.Property(x => x.Label).HasMaxLength(30);
            e.HasOne(x => x.Ward).WithMany(w => w.Beds).HasForeignKey(x => x.WardId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Admission>(e =>
        {
            // The database itself guarantees one active admission per bed and per patient, even under races.
            e.HasIndex(x => x.BedId).IsUnique().HasFilter("[DischargedAt] IS NULL");
            e.HasIndex(x => x.PatientId).IsUnique().HasFilter("[DischargedAt] IS NULL");
            e.Property(x => x.Reason).HasMaxLength(300);
            e.Property(x => x.DischargeSummary).HasMaxLength(2000);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Bed).WithMany().HasForeignKey(x => x.BedId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<VitalSign>(e =>
        {
            e.HasIndex(x => new { x.PatientId, x.RecordedAt });
            e.Property(x => x.TemperatureC).HasPrecision(4, 1);
            e.Property(x => x.Severity).HasMaxLength(10);
            e.Property(x => x.AlertReason).HasMaxLength(300);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RecordedBy).WithMany().HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Notification>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.Property(x => x.Type).HasMaxLength(20);
            e.Property(x => x.Message).HasMaxLength(500);
        });
        b.Entity<QueueToken>(e =>
        {
            e.HasIndex(x => new { x.Date, x.DoctorId, x.Number }).IsUnique();
            e.Property(x => x.Status).HasMaxLength(10);
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<DoctorSchedule>(e =>
        {
            e.HasIndex(x => new { x.DoctorId, x.Day });
            e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AttendanceRecord>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Date }).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(40);
            e.Property(x => x.Entity).HasMaxLength(40);
            e.Property(x => x.EntityId).HasMaxLength(64);
            e.Property(x => x.Ip).HasMaxLength(45);
        });
    }
}
