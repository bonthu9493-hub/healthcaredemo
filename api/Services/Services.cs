using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HospitalApi.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

namespace HospitalApi.Services;

public static class ClaimsExtensions
{
    public static Guid UserId(this ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue("sub") ?? throw new UnauthorizedAccessException());
    public static string RoleName(this ClaimsPrincipal p) => p.FindFirstValue("role") ?? "";
}

public class TokenService(IConfiguration cfg)
{
    public const int LifetimeMinutes = 30;

    public (string Token, DateTimeOffset Expires) Create(AppUser user)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(LifetimeMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
        var token = new JwtSecurityToken(
            issuer: cfg["Jwt:Issuer"],
            audience: cfg["Jwt:Audience"],
            claims:
            [
                new Claim("sub", user.Id.ToString()),
                new Claim("role", user.Role),
                new Claim("name", user.FullName),
            ],
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

public class AuditService(AppDbContext db, IHttpContextAccessor http)
{
    public async Task LogAsync(string action, string entity, string? entityId = null)
    {
        var ctx = http.HttpContext;
        Guid? uid = Guid.TryParse(ctx?.User.FindFirstValue("sub"), out var g) ? g : null;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = uid, Action = action, Entity = entity, EntityId = entityId,
            Ip = ctx?.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync();
    }
}

/// <summary>SignalR addresses users by the JWT "sub" claim.</summary>
public class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext c) => c.User?.FindFirstValue("sub");
}
