using System.IdentityModel.Tokens.Jwt;
using System.Text;
using CsCheck;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Enums;
using StreamingPanel.Core.Interfaces;
using StreamingPanel.Infrastructure.Security;

namespace StreamingPanel.Tests.Security;

/// <summary>
/// Property-based tests for <see cref="JwtTokenGenerator"/>.
///
/// Feature: streaming-panel, Property 5: Access tokens embed identity and role claims with correct lifetimes
/// Validates: Requirements 2.2, 2.3, 2.4, 3.1
/// </summary>
public class JwtTokenGeneratorPropertyTests
{
    // 32-byte (256-bit) HMAC signing key — the minimum HS256 requires.
    private const string SigningKey = "streaming-panel-test-signing-key-0123456789";
    private const string Issuer = "streaming-panel-tests";
    private const string Audience = "streaming-panel-clients";

    private static JwtTokenGenerator CreateGenerator()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
            })
            .Build();

        return new JwtTokenGenerator(configuration);
    }

    // Generates a Person with random id, email, and role spanning the full input space.
    private static readonly Gen<Person> GenPerson =
        from id in Gen.Guid
        from local in Gen.String[Gen.Char.AlphaNumeric, 1, 20]
        from domain in Gen.String[Gen.Char.AlphaNumeric, 1, 20]
        from tld in Gen.OneOfConst("com", "net", "org", "io")
        from role in Gen.Enum<Role>()
        select new Person
        {
            Id = id,
            Email = $"{local}@{domain}.{tld}",
            Role = role,
            Name = "Test Person",
            PasswordHash = "irrelevant",
        };

    /// <summary>
    /// Feature: streaming-panel, Property 5: Access tokens embed identity and role claims with correct lifetimes
    ///
    /// For any Person, the issued access token decodes to claims whose subject, email, and role
    /// match that Person, with an expiry of issuance + 15 minutes; and the paired refresh token
    /// has an expiry of issuance + 7 days, carries a non-empty raw token, and persists only a hash
    /// (TokenHash != raw token).
    ///
    /// Validates: Requirements 2.2, 2.3, 2.4, 3.1
    /// </summary>
    [Fact]
    public void AccessAndRefreshTokens_EmbedClaimsWithCorrectLifetimes()
    {
        var generator = CreateGenerator();

        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = false, // we assert the exact expiry explicitly below
            ClockSkew = TimeSpan.Zero,
        };

        GenPerson.Sample(person =>
        {
            var handler = new JwtSecurityTokenHandler();

            // --- Access token (R2.2, R2.3) ---
            var issuedAt = DateTime.UtcNow;
            var accessToken = generator.CreateAccessToken(person);

            // Signature + issuer + audience must validate (R2.2 — HMAC-signed, correct claims).
            var principal = handler.ValidateToken(accessToken, validationParameters, out var validated);
            var jwt = (JwtSecurityToken)validated;

            // sub == person id.
            var sub = jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value;
            Assert.Equal(person.Id.ToString(), sub);

            // email claim == person email. The handler maps the "email" claim to a longer URI type,
            // so match on either the short or mapped name.
            var email = jwt.Claims
                .First(c => c.Type is JwtRegisteredClaimNames.Email
                    or "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress")
                .Value;
            Assert.Equal(person.Email, email);

            // role claim == person role.
            var role = jwt.Claims.First(c => c.Type == "role").Value;
            Assert.Equal(person.Role.ToString(), role);

            // Expiry ~ issuance + 15 minutes (small tolerance for execution time).
            var expectedAccessExpiry = issuedAt.Add(JwtTokenGenerator.AccessTokenLifetime);
            Assert.True((jwt.ValidTo - expectedAccessExpiry).Duration() <= TimeSpan.FromSeconds(30),
                $"Access token expiry {jwt.ValidTo:o} not within tolerance of {expectedAccessExpiry:o}.");

            // --- Refresh token (R2.3, R2.4) ---
            var refreshIssuedAt = DateTime.UtcNow;
            var refresh = generator.CreateRefreshToken(person.Id);

            // Raw token is non-empty and returned to the caller.
            Assert.False(string.IsNullOrEmpty(refresh.RawToken));

            // Entity owns the person and expires ~ issuance + 7 days.
            Assert.Equal(person.Id, refresh.Entity.PersonId);
            var expectedRefreshExpiry = refreshIssuedAt.Add(JwtTokenGenerator.RefreshTokenLifetime);
            Assert.True((refresh.Entity.ExpiresAt - expectedRefreshExpiry).Duration() <= TimeSpan.FromSeconds(30),
                $"Refresh token expiry {refresh.Entity.ExpiresAt:o} not within tolerance of {expectedRefreshExpiry:o}.");

            // Only the hash is persisted — never the raw token (R2.4).
            Assert.NotEqual(refresh.RawToken, refresh.Entity.TokenHash);
            Assert.False(string.IsNullOrEmpty(refresh.Entity.TokenHash));
        }, iter: 100);
    }
}
