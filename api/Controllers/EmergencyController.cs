using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

[ApiController, Route("api/emergency"), Authorize(Roles = $"{Roles.Doctor},{Roles.Nurse}"), EnableRateLimiting("emergency")]
public class EmergencyController(AppDbContext db, NotificationService notify, AuditService audit) : ControllerBase
{
    /// <summary>Alerts every doctor and nurse on the system. Saved as a notification and pushed live.</summary>
    [HttpPost("code-blue")]
    public async Task<IActionResult> CodeBlue(CodeBlueRequest req)
    {
        var uid = User.UserId();
        var me = await db.Users.AsNoTracking().FirstAsync(u => u.Id == uid);
        var location = req.Location.Trim();

        await notify.SendAsync(await notify.StaffIds(), NotificationType.CodeBlue,
            $"CODE BLUE at {location} — raised by {me.FullName}", exceptUserId: uid);
        await audit.LogAsync("CodeBlue", "Emergency", location);
        return Accepted();
    }
}
