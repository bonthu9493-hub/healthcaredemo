using System.Net;
using System.Net.Http.Json;
using HospitalApi.Dtos;
using Microsoft.IdentityModel.Tokens;

namespace HospitalApi.Tests;

[Collection("api")]
public class AuthTests(ApiFixture api)
{
    [Fact]
    public async Task Register_then_login_gives_a_patient_token()
    {
        var patient = await api.NewPatientAsync();

        var login = await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { patient.Email, patient.Password });
        var auth = await ApiFixture.Read<AuthResponse>(login);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("Patient", auth.Role);
        Assert.Equal(patient.FullName, auth.FullName);
        Assert.True(auth.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_answer()
    {
        var patient = await api.NewPatientAsync();
        var anon = api.Anonymous();

        var wrong = await anon.PostAsJsonAsync("/api/auth/login", new { patient.Email, Password = "WrongPass12345" });
        var unknown = await anon.PostAsJsonAsync("/api/auth/login", new { Email = "nobody@test.local", Password = "WrongPass12345" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        // No hint about whether the account exists.
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_password()
    {
        var patient = await api.NewPatientAsync();
        var anon = api.Anonymous();

        for (var i = 0; i < 5; i++)
            await anon.PostAsJsonAsync("/api/auth/login", new { patient.Email, Password = "WrongPass12345" });

        var correct = await anon.PostAsJsonAsync("/api/auth/login", new { patient.Email, patient.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
    }

    [Theory]
    [InlineData("short1A")]          // too short
    [InlineData("alllowercase123")]  // no upper case
    [InlineData("ALLUPPERCASE123")]  // no lower case
    [InlineData("NoDigitsHereAtAll")]
    public async Task Weak_passwords_are_rejected(string password)
    {
        var response = await api.Anonymous().PostAsJsonAsync("/api/auth/register",
            new { email = $"weak{Guid.NewGuid():N}@test.local", password, fullName = "Weak Pass", phone = "+91 9876543210", dateOfBirth = "1990-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Markup_in_a_name_is_rejected()
    {
        var response = await api.Anonymous().PostAsJsonAsync("/api/auth/register",
            new { email = $"xss{Guid.NewGuid():N}@test.local", password = ApiFixture.StrongPassword,
                  fullName = "<script>alert(1)</script>", phone = "+91 9876543210", dateOfBirth = "1990-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registering_an_existing_email_fails()
    {
        var patient = await api.NewPatientAsync();
        var again = await api.Anonymous().PostAsJsonAsync("/api/auth/register",
            new { email = patient.Email, password = ApiFixture.StrongPassword, fullName = "Copy Cat", phone = "+91 9876543210", dateOfBirth = "1990-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Registration_cannot_choose_a_role()
    {
        var email = $"sneaky{Guid.NewGuid():N}@test.local";
        var register = await api.Anonymous().PostAsJsonAsync("/api/auth/register",
            new { email, password = ApiFixture.StrongPassword, fullName = "Sneaky Admin", phone = "+91 9876543210",
                  dateOfBirth = "1990-01-01", role = "Admin" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = ApiFixture.StrongPassword });
        Assert.Equal("Patient", (await ApiFixture.Read<AuthResponse>(login)).Role);
    }

    [Theory]
    [InlineData("/api/appointments")]
    [InlineData("/api/doctors")]
    [InlineData("/api/records")]
    [InlineData("/api/vitals")]
    [InlineData("/api/notifications")]
    [InlineData("/api/queue/board")]
    [InlineData("/api/admin/users")]
    [InlineData("/api/attendance/me")]
    public async Task Every_data_endpoint_needs_a_login(string url)
    {
        var response = await api.Anonymous().GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_with_an_edited_role_is_rejected()
    {
        var patient = await api.NewPatientAsync();
        var login = await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { patient.Email, patient.Password });
        var token = (await ApiFixture.Read<AuthResponse>(login)).Token;

        // Rewrite the payload to claim the Admin role but keep the original signature.
        var parts = token.Split('.');
        parts[1] = Base64UrlEncoder.Encode(Base64UrlEncoder.Decode(parts[1]).Replace("\"Patient\"", "\"Admin\""));
        var forged = api.Anonymous();
        forged.DefaultRequestHeaders.Authorization = new("Bearer", string.Join('.', parts));

        var response = await forged.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
