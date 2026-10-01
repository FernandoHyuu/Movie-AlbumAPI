using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Interfaces;

namespace StreamingPanel.Infrastructure.Security;

/// <summary>
/// Issues HMAC-signed JWT access tokens and opaque 256-bit refresh tokens. Signing key,
/// issuer, and audience come from configuration. A refresh token is handed back raw but
/// only ever persisted as a SHA-256 hash.
/// </summary>
public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    /// <summary>Access-token lifetime.</summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Refresh-token lifetime.</summary>
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private const int RefreshTokenByteLength = 32; // 256 bits of entropy.

    private readonly string _signingKey;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtTokenGenerator(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Missing required configuration value 'Jwt:SigningKey'.");
        _issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Missing required configuration value 'Jwt:Issuer'.");
        _audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Missing required configuration value 'Jwt:Audience'.");
    }

    /// <inheritdoc />
    public string CreateAccessToken(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);

        var now = DateTime.UtcNow;
        var expires = now.Add(AccessTokenLifetime);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, person.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, person.Email),
            new Claim("role", person.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc />
    public RefreshTokenResult CreateRefreshToken(Guid personId)
    {
        var now = DateTime.UtcNow;

        var rawBytes = RandomNumberGenerator.GetBytes(RefreshTokenByteLength);
        var rawToken = Convert.ToBase64String(rawBytes);

        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            PersonId = personId,
            TokenHash = HashToken(rawToken),
            CreatedAt = now,
            ExpiresAt = now.Add(RefreshTokenLifetime),
        };

        return new RefreshTokenResult(rawToken, entity);
    }

    /// <summary>
    /// Hex-encoded SHA-256 of a raw refresh token. Applied the same way before persisting
    /// and before lookup, so a DB leak never exposes a usable token.
    /// </summary>
    public static string HashToken(string rawToken)
    {
        ArgumentNullException.ThrowIfNull(rawToken);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash);
    }
}
