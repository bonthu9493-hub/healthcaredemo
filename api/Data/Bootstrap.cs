using Microsoft.EntityFrameworkCore;

namespace HospitalApi.Data;

public static class Bootstrap
{
    private static readonly (string Name, decimal Price)[] StarterLabTests =
    [
        ("Complete Blood Count (CBC)", 350m), ("Blood Sugar (Fasting)", 120m), ("Lipid Profile", 600m),
        ("Liver Function Test", 700m), ("Kidney Function Test", 650m), ("Urine Routine", 150m),
        ("ECG", 300m), ("Chest X-Ray", 500m),
    ];

    /// <summary>
    /// Applies migrations, adds a starter list of lab tests the first time, and, if Admin credentials are configured,
    /// creates the first admin once.
    /// </summary>
    public static async Task RunAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        if (!await db.LabTests.AnyAsync())
        {
            db.LabTests.AddRange(StarterLabTests.Select(t => new LabTest { Name = t.Name, Price = t.Price }));
            await db.SaveChangesAsync();
        }

        var cfg = app.Configuration;
        var email = cfg["Admin:Email"]?.Trim().ToLowerInvariant();
        var password = cfg["Admin:Password"];
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)) return;
        if (await db.Users.AnyAsync(u => u.Role == Roles.Admin)) return;

        db.Users.Add(new AppUser
        {
            Email = email, FullName = "System Admin", Role = Roles.Admin,
            PasswordHash = scope.ServiceProvider.GetRequiredService<Services.PasswordService>().Hash(password),
        });
        await db.SaveChangesAsync();
        app.Logger.LogInformation("Initial admin account created. Remove Admin:Password from configuration.");
    }
}
