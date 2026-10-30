using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Authorize]
public class ProfileController(AppDbContext db, AttendanceClock clock, AuditService audit) : ControllerBase
{
    /// <summary>A patient's own full profile.</summary>
    [HttpGet("api/profile/me"), Authorize(Roles = Roles.Patient)]
    public async Task<PatientProfileDto> Mine()
    {
        var uid = User.UserId();
        var p = await db.Patients.AsNoTracking().Include(x => x.User).FirstAsync(x => x.UserId == uid);
        return new PatientProfileDto(p.Id, p.User.FullName, p.DateOfBirth, p.Phone, p.BloodGroup, p.Allergies,
            p.ChronicConditions, p.EmergencyContactName, p.EmergencyContactPhone);
    }

    [HttpPut("api/profile/me"), Authorize(Roles = Roles.Patient)]
    public async Task<PatientProfileDto> Update(UpdateProfileRequest req)
    {
        var uid = User.UserId();
        var p = await db.Patients.Include(x => x.User).FirstAsync(x => x.UserId == uid);
        p.Phone = req.Phone.Trim();
        p.BloodGroup = string.IsNullOrWhiteSpace(req.BloodGroup) ? null : req.BloodGroup;
        p.Allergies = req.Allergies.Trim();
        p.ChronicConditions = req.ChronicConditions.Trim();
        p.EmergencyContactName = req.EmergencyContactName.Trim();
        p.EmergencyContactPhone = req.EmergencyContactPhone.Trim();
        await db.SaveChangesAsync();
        await audit.LogAsync("UpdateProfile", "Patient", p.Id.ToString());
        return new PatientProfileDto(p.Id, p.User.FullName, p.DateOfBirth, p.Phone, p.BloodGroup, p.Allergies,
            p.ChronicConditions, p.EmergencyContactName, p.EmergencyContactPhone);
    }

    /// <summary>
    /// Bedside summary (allergies, blood group, conditions). Nurses may see any patient; doctors only patients they have an
    /// appointment with. It never includes the phone number or date of birth.
    /// </summary>
    [HttpGet("api/patients/{id:guid}/summary"), Authorize(Roles = $"{Roles.Doctor},{Roles.Nurse}")]
    public async Task<ActionResult<PatientSummaryDto>> Summary(Guid id)
    {
        var uid = User.UserId();
        var isNurse = User.RoleName() == Roles.Nurse;
        var p = await db.Patients.AsNoTracking().Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id && (isNurse || db.Appointments.Any(a => a.PatientId == x.Id && a.Doctor.UserId == uid)));
        if (p is null) return NotFound();

        var today = clock.Today();
        var age = today.Year - p.DateOfBirth.Year - (today < p.DateOfBirth.AddYears(today.Year - p.DateOfBirth.Year) ? 1 : 0);
        await audit.LogAsync("ReadPatientSummary", "Patient", p.Id.ToString());
        return new PatientSummaryDto(p.Id, p.User.FullName, age, p.BloodGroup, p.Allergies, p.ChronicConditions);
    }
}
