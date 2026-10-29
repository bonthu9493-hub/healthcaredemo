using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Data;

public static class Bootstrap
{
    /// <summary>Applies migrations and, if Admin credentials are configured, creates the first admin once.</summary>
    public static async Task RunAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var cfg = app.Configuration;
        var email = cfg["Admin:Email"]?.Trim().ToLowerInvariant();
        var password = cfg["Admin:Password"];
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)) return;
        if (await db.Users.AnyAsync(u => u.Role == Roles.Admin)) return;

        db.Users.Add(new AppUser
        {
            Email = email, FullName = "System Admin", Role = Roles.Admin,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
        });
        await db.SaveChangesAsync();
        app.Logger.LogInformation("Initial admin account created. Remove Admin:Password from configuration.");
    }
}
