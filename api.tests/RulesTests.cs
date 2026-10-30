using HospitalApi.Data;
using HospitalApi.Services;
using Microsoft.Extensions.Configuration;

namespace HospitalApi.Tests;

/// <summary>Pure logic, no database or web server.</summary>
public class VitalsRulesTests
{
    [Theory]
    [InlineData(72, 118, 76, 98, 36.8, "Normal")]
    [InlineData(48, 118, 76, 98, 36.8, "Warning")]   // slow heart rate
    [InlineData(115, 118, 76, 98, 36.8, "Warning")]  // fast heart rate
    [InlineData(72, 145, 76, 98, 36.8, "Warning")]   // high systolic
    [InlineData(72, 118, 95, 98, 36.8, "Warning")]   // high diastolic
    [InlineData(72, 118, 76, 93, 36.8, "Warning")]   // low oxygen
    [InlineData(72, 118, 76, 98, 38.5, "Warning")]   // fever
    [InlineData(72, 118, 76, 98, 35.5, "Warning")]   // low temperature
    [InlineData(35, 118, 76, 98, 36.8, "Critical")]
    [InlineData(135, 118, 76, 98, 36.8, "Critical")]
    [InlineData(72, 75, 50, 98, 36.8, "Critical")]   // very low pressure
    [InlineData(72, 185, 76, 98, 36.8, "Critical")]  // very high pressure
    [InlineData(72, 118, 76, 89, 36.8, "Critical")]  // dangerously low oxygen
    [InlineData(72, 118, 76, 98, 40.0, "Critical")]
    [InlineData(72, 118, 76, 98, 34.5, "Critical")]
    public void Severity_follows_the_thresholds(int hr, int sys, int dia, int spo2, double temp, string expected)
    {
        var (severity, reason) = VitalsRules.Evaluate(hr, sys, dia, spo2, (decimal)temp);

        Assert.Equal(expected, severity);
        Assert.Equal(expected == "Normal", reason is null);
    }

    [Fact]
    public void Boundaries_are_inclusive_of_the_normal_range()
    {
        Assert.Equal("Normal", VitalsRules.Evaluate(50, 90, 60, 95, 36.0m).Severity);
        Assert.Equal("Normal", VitalsRules.Evaluate(110, 140, 90, 100, 38.0m).Severity);
    }

    [Fact]
    public void The_reason_lists_every_problem_and_critical_wins()
    {
        var (severity, reason) = VitalsRules.Evaluate(140, 190, 100, 85, 40.0m);

        Assert.Equal("Critical", severity);
        Assert.Contains("heart rate 140", reason);
        Assert.Contains("oxygen 85%", reason);
        Assert.Contains("blood pressure 190/100", reason);
        Assert.Contains("temperature 40", reason);
    }
}

public class SchedulingTests
{
    private static DoctorSchedule Window(DayOfWeek day, string from, string to, int slot = 30) =>
        new() { Day = day, Start = TimeOnly.Parse(from), End = TimeOnly.Parse(to), SlotMinutes = slot };

    private static DateTimeOffset At(DayOfWeek day, string time)
    {
        var d = new DateTime(2026, 11, 2); // a Monday
        d = d.AddDays(((int)day - (int)d.DayOfWeek + 7) % 7);
        return new DateTimeOffset(d.Add(TimeOnly.Parse(time).ToTimeSpan()), TimeSpan.Zero);
    }

    [Theory]
    [InlineData("10:00", true)]
    [InlineData("10:30", true)]
    [InlineData("11:30", true)]   // last slot ends exactly at 12:00
    [InlineData("12:00", false)]  // window is over
    [InlineData("09:30", false)]  // before the window
    [InlineData("10:15", false)]  // between slots
    public void Only_exact_slots_inside_the_window_match(string time, bool expected)
    {
        var schedule = new[] { Window(DayOfWeek.Monday, "10:00", "12:00") };

        Assert.Equal(expected, Scheduling.MatchingEntry(schedule, At(DayOfWeek.Monday, time)) is not null);
    }

    [Fact]
    public void Other_days_do_not_match()
    {
        var schedule = new[] { Window(DayOfWeek.Monday, "10:00", "12:00") };

        Assert.Null(Scheduling.MatchingEntry(schedule, At(DayOfWeek.Tuesday, "10:00")));
    }

    [Fact]
    public void Each_window_uses_its_own_slot_length()
    {
        var schedule = new[] { Window(DayOfWeek.Monday, "09:00", "10:00", 20), Window(DayOfWeek.Monday, "14:00", "16:00", 60) };

        Assert.NotNull(Scheduling.MatchingEntry(schedule, At(DayOfWeek.Monday, "09:40")));
        Assert.Null(Scheduling.MatchingEntry(schedule, At(DayOfWeek.Monday, "14:30")));
        Assert.NotNull(Scheduling.MatchingEntry(schedule, At(DayOfWeek.Monday, "15:00")));
    }
}

public class PasswordServiceTests
{
    private sealed class FakeEnv(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static PasswordService Make(string env, string? factor)
    {
        var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(factor is null ? [] : new Dictionary<string, string?> { ["Security:BcryptWorkFactor"] = factor }).Build();
        return new PasswordService(cfg, new FakeEnv(env));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void Outside_testing_the_strong_setting_cannot_be_lowered_by_configuration(string env) =>
        Assert.Equal(12, Make(env, "4").WorkFactor);

    [Fact]
    public void Only_the_testing_environment_may_use_a_cheaper_setting_and_never_a_silly_one()
    {
        Assert.Equal(4, Make("Testing", "4").WorkFactor);
        Assert.Equal(4, Make("Testing", "1").WorkFactor);    // floor
        Assert.Equal(12, Make("Testing", "99").WorkFactor);  // never above production
        Assert.Equal(12, Make("Testing", null).WorkFactor);
        Assert.Equal(12, Make("Testing", "abc").WorkFactor);
    }

    [Fact]
    public void Hashes_verify_and_are_salted()
    {
        var svc = Make("Testing", "4");
        var a = svc.Hash("Secret12345");
        var b = svc.Hash("Secret12345");

        Assert.NotEqual(a, b);
        Assert.True(BCrypt.Net.BCrypt.Verify("Secret12345", a));
        Assert.False(BCrypt.Net.BCrypt.Verify("secret12345", a));
        Assert.DoesNotContain("Secret12345", a);
    }
}

public class PagingHelperTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  heart  ", "heart")]
    public void Search_terms_are_trimmed_and_empty_ones_ignored(string? input, string? expected) =>
        Assert.Equal(expected, PagingExtensions.CleanSearch(input));

    [Fact]
    public void Very_long_search_terms_are_cut_off() =>
        Assert.Equal(PagingExtensions.MaxSearchLength, PagingExtensions.CleanSearch(new string('x', 5000))!.Length);
}
