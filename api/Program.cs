using System.Text;
using System.Threading.RateLimiting;
using HospitalApi.Data;
using HospitalApi.Hubs;
using HospitalApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ---- Secrets come from user-secrets (dev) or environment variables (prod); nothing is committed. ----
string Require(string key, int minLength = 1) =>
    cfg[key] is { } v && v.Length >= minLength
        ? v
        : throw new InvalidOperationException(
            $"Missing configuration '{key}'. Set it with `dotnet user-secrets set \"{key}\" \"...\"` or the env var {key.Replace(":", "__")}.");

var jwtKey = Require("Jwt:Key", 32);
var connection = Require("ConnectionStrings:Default");
var corsOrigins = Require("Cors:Origins").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;              // don't advertise the server software
    o.Limits.MaxRequestBodySize = 1_000_000; // 1 MB
});

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connection));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<AttendanceClock>();
builder.Services.AddSingleton<IUserIdProvider, SubUserIdProvider>();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = Require("Jwt:Issuer"),
            ValidateAudience = true, ValidAudience = Require("Jwt:Audience"),
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role",
        };
        // Browsers can't set headers on WebSockets, so SignalR sends the token in the query string.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            },
        };
    });

// Secure by default: every endpoint needs a signed-in user unless it opts out with [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(corsOrigins)
    .WithHeaders("Authorization", "Content-Type", "X-Requested-With", "X-SignalR-User-Agent")
    .WithMethods("GET", "POST", "PATCH")
    .AllowCredentials()));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
        RateLimitPartition.GetFixedWindowLimiter(Ip(c), _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
    // Code blue must not be spammable
    o.AddPolicy("emergency", c => RateLimitPartition.GetFixedWindowLimiter(Ip(c), _ => new FixedWindowRateLimiterOptions
    { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
    // Brute-force protection for register/login
    o.AddPolicy("auth", c => RateLimitPartition.GetFixedWindowLimiter(Ip(c), _ => new FixedWindowRateLimiterOptions
    { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

app.UseExceptionHandler();      // generic ProblemDetails; no stack traces in any environment
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    h["Cross-Origin-Resource-Policy"] = "same-site";
    h["Cache-Control"] = "no-store";
    await next();
});

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationsHub>("/hubs/notifications");

await Bootstrap.RunAsync(app);
app.Run();
