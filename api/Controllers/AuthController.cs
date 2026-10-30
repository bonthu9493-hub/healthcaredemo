using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api/auth"), AllowAnonymous, EnableRateLimiting("auth")]
public class AuthController(AppDbContext db, TokenService tokens, AuditService audit, PasswordService passwords) : ControllerBase
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(15);
    // Verified against when the email is unknown so response time doesn't reveal valid accounts.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("dummy-password-for-timing");

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (req.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow) || req.DateOfBirth.Year < 1900)
            return BadRequest(new { error = "Invalid date of birth." });
        if (await db.Users.AnyAsync(u => u.Email == email))
            return BadRequest(new { error = "Registration failed." }); // no account enumeration detail

        var user = new AppUser
        {
            Email = email, FullName = req.FullName.Trim(), Role = Roles.Patient,
            PasswordHash = passwords.Hash(req.Password),
        };
        db.Users.Add(user);
        db.Patients.Add(new Patient { User = user, DateOfBirth = req.DateOfBirth, Phone = req.Phone.Trim() });
        await db.SaveChangesAsync();
        await audit.LogAsync("Register", "User", user.Id.ToString());
        return StatusCode(201);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is null)
        {
            BCrypt.Net.BCrypt.Verify(req.Password, DummyHash);
            return Unauthorized(new { error = "Invalid email or password." });
        }
        if (user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            await audit.LogAsync("LoginBlocked", "User", user.Id.ToString());
            return Unauthorized(new { error = "Invalid email or password." });
        }
        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailures)
            {
                user.LockoutEnd = DateTimeOffset.UtcNow.Add(LockoutTime);
                user.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync();
            await audit.LogAsync("LoginFailed", "User", user.Id.ToString());
            return Unauthorized(new { error = "Invalid email or password." });
        }

        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        await db.SaveChangesAsync();
        await audit.LogAsync("Login", "User", user.Id.ToString());
        var (token, expires) = tokens.Create(user);
        return new AuthResponse(token, expires, user.Role, user.FullName);
    }
}
