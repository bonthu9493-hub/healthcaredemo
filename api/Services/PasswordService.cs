namespace HospitalApi.Services;

/// <summary>
/// Hashes passwords with BCrypt. Production always uses the strong work factor (12). Only the "Testing" environment may
/// choose a cheaper one, so thousands of throwaway test accounts don't take minutes; the setting is ignored everywhere else.
/// </summary>
public class PasswordService(IConfiguration cfg, IHostEnvironment env)
{
    public const int ProductionWorkFactor = 12;

    public int WorkFactor { get; } =
        env.EnvironmentName == "Testing" && int.TryParse(cfg["Security:BcryptWorkFactor"], out var n)
            ? Math.Clamp(n, 4, ProductionWorkFactor)
            : ProductionWorkFactor;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactor);
}
