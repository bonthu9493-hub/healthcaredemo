using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using HospitalApi.Data;
using HospitalApi.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HospitalApi.Tests;

/// <summary>Starts the real API in memory. Every setting is supplied here, so no developer secrets are ever read.</summary>
public sealed class ApiFactory(IDictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing"); // not "Development": user-secrets are not loaded
        foreach (var (key, value) in ApiFixture.BaseSettings()) builder.UseSetting(key, value);
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>()) builder.UseSetting(key, value);
    }
}

public record TestUser(string Email, string Password, string FullName, HttpClient Client, Guid UserId, Guid ProfileId);

/// <summary>
/// One API + one throwaway SQL Server database for the whole test run (deleted afterwards).
/// Point it at a server with TEST_SQL_CONNECTION (without a Database= part); the default is local SQL Express.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private const string DefaultServer = @"Server=localhost\SQLEXPRESS;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True";
    private static readonly string DbName = "HospitalTest_" + Guid.NewGuid().ToString("N")[..12];
    private static readonly string JwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public const string AdminEmail = "admin@test.local";
    public static readonly string AdminPassword = "Aa1" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
    public const string StrongPassword = "TestPass12345";

    public static string ConnectionString =>
        (Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? DefaultServer).TrimEnd(';') + ";Database=" + DbName;

    public static Dictionary<string, string?> BaseSettings() => new()
    {
        ["Jwt:Key"] = JwtKey,
        ["Jwt:Issuer"] = "hospital-api",
        ["Jwt:Audience"] = "hospital-web",
        ["ConnectionStrings:Default"] = ConnectionString,
        ["Cors:Origins"] = "http://localhost:4200",
        ["Admin:Email"] = AdminEmail,
        ["Admin:Password"] = AdminPassword,
        // Tests create many accounts and log in a lot; the rate-limit behaviour has its own test with low limits.
        ["Security:BcryptWorkFactor"] = "4", // honoured only in the "Testing" environment; production is always 12
        ["RateLimit:GlobalPerMinute"] = "100000",
        ["RateLimit:AuthPerMinute"] = "100000",
        ["RateLimit:EmergencyPerMinute"] = "100000",
    };

    public ApiFactory Factory { get; } = new();

    public async Task InitializeAsync()
    {
        using var _ = Factory.CreateClient(); // starts the host: applies migrations and seeds the admin
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        using (var scope = Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        Factory.Dispose();
    }

    // ---------- helpers ----------

    public HttpClient Anonymous() => Factory.CreateClient();

    public async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = Anonymous();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    private HttpClient? _admin;
    public async Task<HttpClient> AdminAsync() => _admin ??= await LoginAsync(AdminEmail, AdminPassword);

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    public async Task<TestUser> NewPatientAsync()
    {
        var id = Unique();
        var (email, name) = ($"patient.{id}@test.local", $"Pat {id}");
        var response = await Anonymous().PostAsJsonAsync("/api/auth/register",
            new { email, password = StrongPassword, fullName = name, phone = "+91 9876543210", dateOfBirth = "1990-05-17" });
        response.EnsureSuccessStatusCode();
        var client = await LoginAsync(email, StrongPassword);
        var (userId, patientId) = await Db(async db =>
        {
            var p = await db.Patients.Include(x => x.User).FirstAsync(x => x.User.Email == email);
            return (p.UserId, p.Id);
        });
        return new TestUser(email, StrongPassword, name, client, userId, patientId);
    }

    public async Task<TestUser> NewDoctorAsync(string specialty = "Cardiology")
    {
        var id = Unique();
        var (email, name) = ($"doctor.{id}@test.local", $"Dr {id}");
        var admin = await AdminAsync();
        var response = await admin.PostAsJsonAsync("/api/admin/doctors",
            new { email, password = StrongPassword, fullName = name, specialty });
        response.EnsureSuccessStatusCode();
        var doctor = (await response.Content.ReadFromJsonAsync<DoctorDto>())!;
        var client = await LoginAsync(email, StrongPassword);
        var userId = await Db(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync());
        return new TestUser(email, StrongPassword, name, client, userId, doctor.Id);
    }

    public async Task<TestUser> NewNurseAsync(string? fullName = null)
    {
        var id = Unique();
        var (email, name) = ($"nurse.{id}@test.local", fullName ?? $"Nurse {id}");
        var admin = await AdminAsync();
        var response = await admin.PostAsJsonAsync("/api/admin/nurses", new { email, password = StrongPassword, fullName = name });
        response.EnsureSuccessStatusCode();
        var client = await LoginAsync(email, StrongPassword);
        var userId = await Db(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync());
        return new TestUser(email, StrongPassword, name, client, userId, userId);
    }

    public async Task<TestUser> NewStaffAsync(string role)
    {
        var id = Unique();
        var (email, name) = ($"{role.ToLowerInvariant()}.{id}@test.local", $"{role} {id}");
        var admin = await AdminAsync();
        var response = await admin.PostAsJsonAsync("/api/admin/staff", new { email, password = StrongPassword, fullName = name, role });
        response.EnsureSuccessStatusCode();
        var client = await LoginAsync(email, StrongPassword);
        var userId = await Db(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync());
        return new TestUser(email, StrongPassword, name, client, userId, userId);
    }

    public Task<TestUser> NewPharmacistAsync() => NewStaffAsync("Pharmacist");
    public Task<TestUser> NewLabTechAsync() => NewStaffAsync("LabTech");

    public async Task<MedicineDto> NewMedicineAsync(TestUser pharmacist, string? name = null, int stock = 100, decimal price = 10m,
        int reorder = 5, DateOnly? expiry = null)
    {
        var response = await pharmacist.Client.PostAsJsonAsync("/api/medicines", new
        {
            name = name ?? $"Med {Unique()}", strength = "500 mg", unitPrice = price, stock, reorderLevel = reorder,
            expiryDate = expiry ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MedicineDto>())!;
    }

    private static readonly Random Rng = new();

    /// <summary>Gives the doctor and patient a treating relationship (an appointment), which unlocks prescribing, ordering, and so on.</summary>
    public async Task TreatAsync(TestUser patient, TestUser doctor)
    {
        var response = await BookAsync(patient, doctor.ProfileId, TestTime.InDays(Rng.Next(20, 400), Rng.Next(8, 20)));
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<InvoiceDto>> InvoicesAsync(HttpClient client, string? status = null) =>
        (await ReadPaged<InvoiceDto>(await client.GetAsync("/api/invoices?pageSize=100" + (status is null ? "" : $"&status={status}")))).Items;

    public static async Task<HospitalApi.Services.PagedResult<T>> ReadPaged<T>(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<HospitalApi.Services.PagedResult<T>>())!;

    public Task<HttpResponseMessage> BookAsync(TestUser patient, Guid doctorId, DateTimeOffset when, string reason = "Routine checkup") =>
        patient.Client.PostAsJsonAsync("/api/appointments", new { doctorId, startsAt = when, reason });

    /// <summary>The API refuses to book the past, so past appointments (for no-show tests) are inserted directly.</summary>
    public Task<Guid> AddPastAppointmentAsync(TestUser patient, TestUser doctor, TimeSpan ago) => Db(async db =>
    {
        var appt = new Appointment
        {
            PatientId = patient.ProfileId, DoctorId = doctor.ProfileId, StartsAt = DateTimeOffset.UtcNow - ago,
            Reason = "Past visit",
        };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();
        return appt.Id;
    });

    public static async Task<T> Read<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>())!;
}

public static class TestTime
{
    /// <summary>A UTC moment at the given hour, N days from today.</summary>
    public static DateTimeOffset InDays(int days, int hour = 10) =>
        new(DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(days).AddHours(hour), TimeSpan.Zero);
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture>;
