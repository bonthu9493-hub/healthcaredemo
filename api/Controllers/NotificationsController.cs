using HospitalApi.Dtos;
using HospitalApi.Data;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Controllers;

/// <summary>Every query is filtered by the caller's own id, so nobody can read or clear someone else's notifications.</summary>
[ApiController, Route("api/notifications"), Authorize]
public class NotificationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<NotificationList> List()
    {
        var uid = User.UserId();
        var items = await db.Notifications.AsNoTracking().Where(n => n.UserId == uid)
            .OrderByDescending(n => n.CreatedAt).Take(50)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Message, n.CreatedAt, n.ReadAt != null)).ToListAsync();
        var unread = await db.Notifications.CountAsync(n => n.UserId == uid && n.ReadAt == null);
        return new NotificationList(items, unread);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var uid = User.UserId();
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);
        if (n is null) return NotFound();
        n.ReadAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var uid = User.UserId();
        await db.Notifications.Where(n => n.UserId == uid && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow));
        return NoContent();
    }
}
