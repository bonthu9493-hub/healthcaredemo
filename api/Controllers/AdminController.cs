using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api/admin"), Authorize(Roles = Roles.Admin)]
public class AdminController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpPost("doctors")]
    public async Task<ActionResult<DoctorDto>> CreateDoctor(CreateDoctorRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email))
            return BadRequest(new { error = "Email already in use." });

        var user = new AppUser
        {
            Email = email, FullName = req.FullName.Trim(), Role = Roles.Doctor,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 12),
        };
        var doctor = new Doctor { User = user, Specialty = req.Specialty.Trim() };
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        await audit.LogAsync("CreateDoctor", "Doctor", doctor.Id.ToString());
        return StatusCode(201, new DoctorDto(doctor.Id, user.FullName, doctor.Specialty));
    }

    [HttpGet("users")]
    public async Task<IEnumerable<UserDto>> Users() =>
        await db.Users.AsNoTracking().OrderBy(u => u.FullName)
            .Select(u => new UserDto(u.Id, u.Email, u.FullName, u.Role)).ToListAsync();

    [HttpGet("audit")]
    public async Task<IEnumerable<AuditDto>> Audit() =>
        await db.AuditLogs.AsNoTracking().OrderByDescending(a => a.At).Take(200)
            .Select(a => new AuditDto(a.Id, a.UserId, a.Action, a.Entity, a.EntityId, a.Ip, a.At)).ToListAsync();
}
