using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Erp.Application.Abstractions;
using Erp.Modules.Platform.Application;
using Erp.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;

namespace Erp.Api.Authentication;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public UserId? UserId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? new UserId(id) : null;
        }
    }

    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;
}

public sealed class HttpAuditRequestContext(IHttpContextAccessor accessor) : IAuditRequestContext
{
    public string CorrelationId =>
        accessor.HttpContext?.Request.Headers["X-Correlation-Id"].SingleOrDefault()
        ?? accessor.HttpContext?.TraceIdentifier
        ?? "system";

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

public sealed class JwtTokenService(IConfiguration configuration, byte[] signingKey)
{
    public string CreateToken(AuthenticatedUser user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(JwtRegisteredClaimNames.Email, user.Email)
        };
        if (user.IsBootstrapAdministrator)
            claims.Add(new("bootstrap_admin", "true"));

        var credentials = new SigningCredentials(new SymmetricSecurityKey(signingKey), SecurityAlgorithms.HmacSha256);
        var issuer = configuration["Authentication:Jwt:Issuer"] ?? "gtek-erp";
        var audience = configuration["Authentication:Jwt:Audience"] ?? "gtek-erp-api";
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

}

public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public static class AuthenticationRegistration
{
    public static IServiceCollection AddLocalAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IAuditRequestContext, HttpAuditRequestContext>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        var signingKey = LoadSigningKey(configuration, environment);
        services.AddSingleton(signingKey);
        services.AddSingleton<JwtTokenService>();
        var issuer = configuration["Authentication:Jwt:Issuer"] ?? "gtek-erp";
        var audience = configuration["Authentication:Jwt:Audience"] ?? "gtek-erp-api";
        services.AddAuthentication("Bearer")
            .AddJwtBearer("Bearer", options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKey),
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.UniqueName,
                    RoleClaimType = "bootstrap_admin"
                };
            });
        services.AddAuthorizationBuilder();
        return services;
    }

    private static byte[] LoadSigningKey(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Authentication:Jwt:SigningKey"];
        if (!string.IsNullOrWhiteSpace(configured))
            return Convert.FromBase64String(configured);
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("Authentication:Jwt:SigningKey must be configured outside Development.");
        return RandomNumberGenerator.GetBytes(32);
    }
}
