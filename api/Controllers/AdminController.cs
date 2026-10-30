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

    [HttpPost("nurses")]
    public async Task<ActionResult<UserDto>> CreateNurse(CreateNurseRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email))
            return BadRequest(new { error = "Email already in use." });

        var user = new AppUser
        {
            Email = email, FullName = req.FullName.Trim(), Role = Roles.Nurse,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 12),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await audit.LogAsync("CreateNurse", "User", user.Id.ToString());
        return StatusCode(201, new UserDto(user.Id, user.Email, user.FullName, user.Role));
    }

    [HttpGet("users")]
    public async Task<PagedResult<UserDto>> Users(string? q, string? role, int page = 1, int pageSize = 20)
    {
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(role)) query = query.Where(u => u.Role == role);
        if (PagingExtensions.CleanSearch(q) is { } term)
            query = query.Where(u => u.FullName.Contains(term) || u.Email.Contains(term));
        return await query.OrderBy(u => u.FullName)
            .Select(u => new UserDto(u.Id, u.Email, u.FullName, u.Role)).ToPagedAsync(page, pageSize);
    }

    [HttpGet("audit")]
    public async Task<PagedResult<AuditDto>> Audit(string? q, int page = 1, int pageSize = 50)
    {
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (PagingExtensions.CleanSearch(q) is { } term)
            query = query.Where(a => a.Action.Contains(term) || a.Entity.Contains(term));
        return await query.OrderByDescending(a => a.At)
            .Select(a => new AuditDto(a.Id, a.UserId, a.Action, a.Entity, a.EntityId, a.Ip, a.At)).ToPagedAsync(page, pageSize);
    }
}
