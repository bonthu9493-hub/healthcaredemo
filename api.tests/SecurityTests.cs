using System.Net;
using System.Net.Http.Json;

namespace HospitalApi.Tests;

[Collection("api")]
public class SecurityTests(ApiFixture api)
{
    [Fact]
    public async Task Responses_carry_the_security_headers()
    {
        var response = await api.Anonymous().GetAsync("/api/doctors"); // even a 401 must have them
        var h = response.Headers;

        Assert.Equal("nosniff", h.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", h.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", h.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", h.GetValues("Content-Security-Policy").Single());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(h.Contains("X-Powered-By"));
    }

    [Fact]
    public async Task Cors_allows_the_frontend_origin_only()
    {
        async Task<HttpResponseMessage> Preflight(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, "/api/doctors");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "GET");
            return await api.Anonymous().SendAsync(request);
        }

        var allowed = await Preflight("http://localhost:4200");
        var evil = await Preflight("https://evil.example.com");

        Assert.Equal("http://localhost:4200", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(evil.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]    // profile and doctor schedule save with PUT; a browser silently blocks it if CORS doesn't allow it
    [InlineData("PATCH")]
    public async Task Cors_allows_every_method_the_web_app_actually_uses(string method)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/profile/me");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await api.Anonymous().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(method, response.Headers.GetValues("Access-Control-Allow-Methods").Single().Split(','));
    }

    [Fact]
    public async Task Cors_still_refuses_methods_the_web_app_never_uses()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/profile/me");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "DELETE");

        var response = await api.Anonymous().SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Methods") && response.Headers.GetValues("Access-Control-Allow-Methods").Single().Contains("DELETE"));
    }

    [Fact]
    public async Task Login_is_rate_limited()
    {
        // Own app instance with a tiny limit, so the shared tests keep their generous one.
        using var strict = new ApiFactory(new Dictionary<string, string?> { ["RateLimit:AuthPerMinute"] = "3" });
        var client = strict.CreateClient();

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            codes.Add((await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@test.local", password = "WrongPass12345" })).StatusCode);

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
                      HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], codes);
    }

    [Fact]
    public async Task Errors_do_not_leak_internals()
    {
        var response = await api.Anonymous().PostAsync("/api/auth/login",
            new StringContent("{ this is not json", System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("at HospitalApi", body);
        Assert.DoesNotContain("System.", body);
    }
}
