using HospitalApi.Data;
using HospitalApi.Dtos;
using HospitalApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Services;

/// <summary>Saves a notification for each recipient (so it survives a refresh) and pushes it live.</summary>
public class NotificationService(AppDbContext db, IHubContext<NotificationsHub> hub)
{
    public async Task SendAsync(IEnumerable<Guid> userIds, string type, string message, Guid? exceptUserId = null)
    {
        var ids = userIds.Where(i => i != exceptUserId).Distinct().ToList();
        if (ids.Count == 0) return;

        var rows = ids.Select(id => new Notification { UserId = id, Type = type, Message = message }).ToList();
        db.Notifications.AddRange(rows);
        await db.SaveChangesAsync();

        foreach (var r in rows)
            await hub.Clients.User(r.UserId.ToString())
                .SendAsync("notification", new NotificationDto(r.Id, r.Type, r.Message, r.CreatedAt, false));
    }

    public Task<List<Guid>> NurseIds() =>
        db.Users.AsNoTracking().Where(u => u.Role == Roles.Nurse).Select(u => u.Id).ToListAsync();

    public Task<List<Guid>> UserIdsInRole(string role) =>
        db.Users.AsNoTracking().Where(u => u.Role == role).Select(u => u.Id).ToListAsync();

    public Task<List<Guid>> StaffIds() =>
        db.Users.AsNoTracking().Where(u => u.Role == Roles.Doctor || u.Role == Roles.Nurse).Select(u => u.Id).ToListAsync();

    /// <summary>Doctors who have an appointment with this patient (the ones allowed to see their data).</summary>
    public Task<List<Guid>> DoctorIdsFor(Guid patientId) =>
        db.Appointments.AsNoTracking().Where(a => a.PatientId == patientId)
            .Select(a => a.Doctor.UserId).Distinct().ToListAsync();
}

/// <summary>Clinical thresholds for vital signs. Evaluated on the server, never trusted from the client.</summary>
public static class VitalsRules
{
    public static (string Severity, string? Reason) Evaluate(int hr, int sys, int dia, int spo2, decimal temp)
    {
        var critical = new List<string>();
        var warning = new List<string>();

        if (hr < 40 || hr > 130) critical.Add($"heart rate {hr}");
        else if (hr < 50 || hr > 110) warning.Add($"heart rate {hr}");

        if (spo2 < 90) critical.Add($"oxygen {spo2}%");
        else if (spo2 < 95) warning.Add($"oxygen {spo2}%");

        if (sys < 80 || sys > 180 || dia > 120) critical.Add($"blood pressure {sys}/{dia}");
        else if (sys < 90 || sys > 140 || dia > 90) warning.Add($"blood pressure {sys}/{dia}");

        if (temp < 35m || temp > 39.5m) critical.Add($"temperature {temp:0.#}°C");
        else if (temp < 36m || temp > 38m) warning.Add($"temperature {temp:0.#}°C");

        if (critical.Count > 0) return (Severity.Critical, string.Join(", ", critical.Concat(warning)));
        if (warning.Count > 0) return (Severity.Warning, string.Join(", ", warning));
        return (Severity.Normal, null);
    }
}

/// <summary>Checks that a requested appointment time is one of the doctor's generated slots.</summary>
public static class Scheduling
{
    public static DoctorSchedule? MatchingEntry(IEnumerable<DoctorSchedule> entries, DateTimeOffset localStart)
    {
        var time = TimeOnly.FromDateTime(localStart.DateTime);
        return entries.FirstOrDefault(e =>
            e.Day == localStart.DayOfWeek
            && time >= e.Start
            && time.AddMinutes(e.SlotMinutes) <= e.End
            && (time.ToTimeSpan() - e.Start.ToTimeSpan()).TotalMinutes % e.SlotMinutes == 0);
    }
}
