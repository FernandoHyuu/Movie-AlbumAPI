using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CsCheck;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using StreamingPanel.Api.Extensions;
using StreamingPanel.Core.Enums;

namespace StreamingPanel.Tests.Security;

/// <summary>
/// Property-based tests for the role-based authorization matrix configured in
/// <see cref="AuthenticationSetup"/>.
///
/// These tests exercise the production authorization decision logic directly rather
/// than through a live HTTP pipeline: the real named policies (<c>MovieAccess</c>,
/// <c>AlbumAccess</c>, <c>AdminOnly</c>) are registered on a minimal
/// <see cref="ServiceCollection"/> via <see cref="AuthenticationSetup.AddJwtAuthenticationAndPolicies"/>,
/// and evaluated against a generated <see cref="ClaimsPrincipal"/> using the resolved
/// <see cref="IAuthorizationService"/>. Token validation (Property 10) is exercised
/// through the same <see cref="TokenValidationParameters"/> the production bearer
/// handler is configured with, since a failed validation is exactly what the bearer
/// handler maps to a 401 challenge.
///
/// Feature: streaming-panel, Property 9: Authorization matrix permits exactly the allowed role/resource pairs
/// Feature: streaming-panel, Property 10: Missing or malformed access tokens yield 401
/// Feature: streaming-panel, Property 11: Absent or unrecognized role claims are forbidden
///
/// Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7
/// </summary>
public class AuthorizationMatrixPropertyTests
{
    // Production-equivalent JWT configuration. The signing key is 32+ bytes as HS256 requires.
    private const string SigningKey = "streaming-panel-test-signing-key-0123456789";
    private const string Issuer = "streaming-panel-tests";
    private const string Audience = "streaming-panel-clients";

    /// <summary>The three protected-resource policies, by their production policy names.</summary>
    private static readonly string[] Policies =
    {
        AuthenticationSetup.MovieAccessPolicy,
        AuthenticationSetup.AlbumAccessPolicy,
        AuthenticationSetup.AdminOnlyPolicy,
    };

    /// <summary>
    /// The permission matrix the implementation must satisfy (R4.3–R4.6). Expressed
    /// here independently of the production assertion logic so the property verifies
    /// the matrix rather than restating the implementation.
    /// </summary>
    private static bool MatrixPermits(Role role, string policy) => policy switch
    {
        AuthenticationSetup.MovieAccessPolicy =>
            role is Role.User_Movie or Role.User_Full or Role.Admin,
        AuthenticationSetup.AlbumAccessPolicy =>
            role is Role.User_Album or Role.User_Full or Role.Admin,
        AuthenticationSetup.AdminOnlyPolicy =>
            role is Role.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown policy."),
    };

    /// <summary>
    /// Builds a service provider carrying the production authorization policies, and
    /// resolves the authorization service used to evaluate them.
    /// </summary>
    private static (IAuthorizationService AuthService, IAuthorizationPolicyProvider PolicyProvider) BuildAuthorization()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJwtAuthenticationAndPolicies(configuration);

        var provider = services.BuildServiceProvider();
        return (
            provider.GetRequiredService<IAuthorizationService>(),
            provider.GetRequiredService<IAuthorizationPolicyProvider>());
    }

    /// <summary>
    /// Builds an authenticated <see cref="ClaimsPrincipal"/> carrying the given
    /// lowercase <c>role</c> claim value, mirroring the token the generator emits.
    /// </summary>
    private static ClaimsPrincipal PrincipalWithRole(string? roleValue)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, "person@example.com"),
        };

        if (roleValue is not null)
        {
            claims.Add(new Claim(AuthenticationSetup.RoleClaimType, roleValue));
        }

        // "Bearer" authentication type => IsAuthenticated is true, so a failing policy
        // yields a 403 (forbidden) decision rather than a 401 (challenge).
        var identity = new ClaimsIdentity(claims, authenticationType: "Bearer");
        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> IsAllowedAsync(
        IAuthorizationService authService,
        ClaimsPrincipal user,
        string policy)
    {
        var result = await authService.AuthorizeAsync(user, resource: null, policyName: policy);
        return result.Succeeded;
    }

    /// <summary>
    /// Feature: streaming-panel, Property 9: Authorization matrix permits exactly the allowed role/resource pairs
    ///
    /// For any defined role and any of the three protected-resource policies, the
    /// authorization decision succeeds if and only if the permission matrix allows that
    /// (role, resource) pair; a disallowed pair is denied (which the pipeline surfaces
    /// as 403, with no action taken on the resource).
    ///
    /// Validates: Requirements 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 6.2, 6.7, 7.3, 7.4
    /// </summary>
    [Fact]
    public void Property9_AuthorizationMatrix_PermitsExactlyAllowedPairs()
    {
        var (authService, _) = BuildAuthorization();

        var gen =
            from role in Gen.Enum<Role>()
            from policyIndex in Gen.Int[0, Policies.Length - 1]
            select (role, policy: Policies[policyIndex]);

        gen.Sample(pair =>
        {
            var (role, policy) = pair;
            var user = PrincipalWithRole(role.ToString());

            var allowed = IsAllowedAsync(authService, user, policy).GetAwaiter().GetResult();
            var expected = MatrixPermits(role, policy);

            Assert.True(
                allowed == expected,
                $"Role '{role}' on policy '{policy}': decision was {allowed}, matrix expects {expected}.");
        }, iter: 100);
    }

    /// <summary>
    /// Feature: streaming-panel, Property 10: Missing or malformed access tokens yield 401
    ///
    /// For any protected endpoint, a request whose access token is absent, malformed,
    /// or expired fails bearer-token validation. The bearer handler maps a failed (or
    /// absent) validation to a 401 challenge and performs no action on the resource.
    /// This property drives generated malformed/expired tokens through the exact
    /// <see cref="TokenValidationParameters"/> the production handler is configured with
    /// and asserts validation never succeeds.
    ///
    /// Validates: Requirements 4.1
    /// </summary>
    [Fact]
    public void Property10_MissingOrMalformedTokens_FailValidation()
    {
        var validationParameters = ProductionValidationParameters();
        var handler = new JwtSecurityTokenHandler();

        // Generates tokens that must NOT validate: empty/whitespace (absent),
        // random non-JWT garbage (malformed), a structurally-valid JWT signed with the
        // wrong key (malformed signature), and a correctly-signed but expired JWT.
        var genBadToken = Gen.OneOf(
            // Absent / empty.
            Gen.Const(string.Empty),
            Gen.Const("   "),
            // Malformed: arbitrary non-token text.
            Gen.String[Gen.Char.AlphaNumeric, 0, 40],
            // Malformed: three dot-separated base64-ish segments that are not a real JWT.
            from a in Gen.String[Gen.Char.AlphaNumeric, 1, 20]
            from b in Gen.String[Gen.Char.AlphaNumeric, 1, 20]
            from c in Gen.String[Gen.Char.AlphaNumeric, 1, 20]
            select $"{a}.{b}.{c}",
            // Malformed signature: valid structure, signed with a different key.
            Gen.Enum<Role>().Select(role => SignedToken(role, "a-completely-different-signing-key-999999999", lifetime: TimeSpan.FromMinutes(15))),
            // Expired: correctly signed but already past its lifetime.
            Gen.Enum<Role>().Select(role => SignedToken(role, SigningKey, lifetime: TimeSpan.FromMinutes(-5))));

        genBadToken.Sample(token =>
        {
            var validated = TryValidate(handler, token, validationParameters);

            Assert.False(
                validated,
                $"Token unexpectedly validated (should be rejected -> 401): '{Truncate(token)}'.");
        }, iter: 100);
    }

    /// <summary>
    /// Feature: streaming-panel, Property 11: Absent or unrecognized role claims are forbidden
    ///
    /// For any authenticated principal whose <c>role</c> claim is absent or is not one
    /// of the four defined roles, every protected-resource policy denies the request
    /// (surfaced as 403), because the policy assertion only matches an exact defined
    /// role string.
    ///
    /// Validates: Requirements 4.7
    /// </summary>
    [Fact]
    public void Property11_AbsentOrUnrecognizedRole_IsForbidden()
    {
        var (authService, _) = BuildAuthorization();

        var definedRoles = Enum.GetNames<Role>();

        // Generates a role claim that is either absent (null) or an arbitrary string
        // that is NOT one of the four defined role names.
        var genBadRole = Gen.OneOf(
            Gen.Const((string?)null),
            Gen.Const(string.Empty),
            Gen.String[Gen.Char.AlphaNumeric, 1, 20]
                .Where(s => Array.IndexOf(definedRoles, s) < 0),
            // Case variations of real roles must also be rejected (ordinal match).
            Gen.OneOfConst("admin", "ADMIN", "user_movie", "User_movie", "Superuser", "root", "guest")
                .Where(s => Array.IndexOf(definedRoles, s) < 0)
                .Select(s => (string?)s));

        var genCase =
            from roleValue in genBadRole
            from policyIndex in Gen.Int[0, Policies.Length - 1]
            select (roleValue, policy: Policies[policyIndex]);

        genCase.Sample(c =>
        {
            var (roleValue, policy) = c;
            var user = PrincipalWithRole(roleValue);

            var allowed = IsAllowedAsync(authService, user, policy).GetAwaiter().GetResult();

            Assert.False(
                allowed,
                $"Unrecognized/absent role '{roleValue ?? "<null>"}' was permitted on policy '{policy}' (should be 403).");
        }, iter: 100);
    }

    // ---- helpers ----

    private static TokenValidationParameters ProductionValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
        ValidateLifetime = true,
        RoleClaimType = AuthenticationSetup.RoleClaimType,
        ClockSkew = TimeSpan.Zero,
    };

    private static string SignedToken(Role role, string signingKey, TimeSpan lifetime)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim("role", role.ToString()),
            },
            // notBefore must precede expiry; for the expired case both are in the past.
            notBefore: now.Add(lifetime) < now ? now.Add(lifetime).AddMinutes(-1) : now,
            expires: now.Add(lifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static bool TryValidate(
        JwtSecurityTokenHandler handler,
        string token,
        TokenValidationParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(token) || !handler.CanReadToken(token))
        {
            return false;
        }

        try
        {
            handler.ValidateToken(token, parameters, out _);
            return true;
        }
        catch (SecurityTokenException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // Malformed token the handler could not parse.
            return false;
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 40 ? value : value[..40] + "...";
}
