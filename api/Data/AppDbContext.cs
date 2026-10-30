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
