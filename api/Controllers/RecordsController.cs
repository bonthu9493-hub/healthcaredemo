using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api"), Authorize]
public class RecordsController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>Patients: own records. Doctors: records of patients they have an appointment with. Admins: none (clinical data is not admin-readable).</summary>
    [HttpGet("records"), Authorize(Roles = $"{Roles.Patient},{Roles.Doctor}")]
    public async Task<PagedResult<RecordDto>> List([FromQuery(Name = "q")] string? search, int page = 1, int pageSize = 20)
    {
        var uid = User.UserId();
        var q = db.MedicalRecords.AsNoTracking();
        q = User.RoleName() == Roles.Doctor
            ? q.Where(r => db.Appointments.Any(a => a.Doctor.UserId == uid && a.PatientId == r.PatientId))
            : q.Where(r => r.Patient.UserId == uid);

        if (PagingExtensions.CleanSearch(search) is { } term)
            q = q.Where(r => r.Diagnosis.Contains(term) || r.Prescription.Contains(term) || r.Patient.User.FullName.Contains(term));

        var list = await q.OrderByDescending(r => r.CreatedAt)
            .Select(r => new RecordDto(r.Id, r.PatientId, r.Patient.User.FullName, r.Doctor.User.FullName,
                r.Diagnosis, r.Prescription, r.Notes, r.CreatedAt)).ToPagedAsync(page, pageSize);
        await audit.LogAsync("ReadRecords", "MedicalRecord");
        return list;
    }

    [HttpPost("records"), Authorize(Roles = Roles.Doctor)]
    public async Task<ActionResult<RecordDto>> Create(CreateRecordRequest req)
    {
        var uid = User.UserId();
        var doctor = await db.Doctors.Include(d => d.User).FirstAsync(d => d.UserId == uid);
        var patient = await db.Patients.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == req.PatientId
                && db.Appointments.Any(a => a.DoctorId == doctor.Id && a.PatientId == p.Id));
        if (patient is null) return NotFound(); // same answer for "no such patient" and "not your patient"

        var rec = new MedicalRecord
        {
            Patient = patient, Doctor = doctor, Diagnosis = req.Diagnosis.Trim(),
            Prescription = req.Prescription.Trim(), Notes = req.Notes.Trim(),
        };
        db.MedicalRecords.Add(rec);
        await db.SaveChangesAsync();
        await audit.LogAsync("CreateRecord", "MedicalRecord", rec.Id.ToString());
        return StatusCode(201, new RecordDto(rec.Id, patient.Id, patient.User.FullName, doctor.User.FullName,
            rec.Diagnosis, rec.Prescription, rec.Notes, rec.CreatedAt));
    }

    /// <summary>Doctors see only patients they have an appointment with.</summary>
    [HttpGet("patients"), Authorize(Roles = Roles.Doctor)]
    public async Task<IEnumerable<PatientDto>> MyPatients()
    {
        var uid = User.UserId();
        return await db.Patients.AsNoTracking()
            .Where(p => db.Appointments.Any(a => a.Doctor.UserId == uid && a.PatientId == p.Id))
            .OrderBy(p => p.User.FullName)
            .Select(p => new PatientDto(p.Id, p.User.FullName, p.DateOfBirth, p.Phone)).ToListAsync();
    }
}
