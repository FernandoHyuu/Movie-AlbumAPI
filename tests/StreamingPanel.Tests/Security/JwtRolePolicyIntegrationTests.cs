using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StreamingPanel.Api.Extensions;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Enums;
using StreamingPanel.Infrastructure.Security;

namespace StreamingPanel.Tests.Security;

/// <summary>
/// Integration-style tests that exercise the REAL token-validation + authorization
/// path end to end, rather than hand-building a <see cref="ClaimsPrincipal"/> with a
/// literal <c>role</c> claim (which bypasses the bearer handler's claim remapping and
/// is exactly why the existing matrix tests did not catch the 403-for-Admin bug).
///
/// Feature: streaming-panel, JWT role-claim policy integration (real validation path)
/// Validates: Requirements 4.2, 4.3, 4.4, 4.5, 4.6, 4.7
///
/// <para>
/// The pipeline reproduced here is: issue a real access token with the production
/// <see cref="JwtTokenGenerator"/>; resolve the exact <see cref="JwtBearerOptions"/>
/// configured by <see cref="AuthenticationSetup.AddJwtAuthenticationAndPolicies"/>
/// (including <see cref="JwtBearerOptions.MapInboundClaims"/>); validate the token
/// through those options' <see cref="Microsoft.IdentityModel.Tokens.TokenValidationParameters"/>
/// using a handler that honors the configured inbound-claim mapping — producing a
/// <see cref="ClaimsPrincipal"/> exactly as the authentication middleware would; and
/// evaluate the real named policies via <see cref="IAuthorizationService"/>.
/// </para>
///
/// <para>
/// The key guard: an Admin token, after passing through the configured validation,
/// must satisfy <c>MovieAccess</c>. With the default (enabled) inbound claim mapping
/// the <c>role</c> claim is renamed to its long WS-* URI and the policy's
/// <c>FindFirst("role")</c> returns null, forbidding Admin (403). This test fails
/// before the <c>MapInboundClaims = false</c> fix and passes after it.
/// </para>
/// </summary>
public class JwtRolePolicyIntegrationTests
{
    // Production-equivalent JWT configuration. The signing key is 32+ bytes as HS256 requires.
    private const string SigningKey = "streaming-panel-test-signing-key-0123456789";
    private const string Issuer = "streaming-panel-tests";
    private const string Audience = "streaming-panel-clients";

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
            })
            .Build();

    /// <summary>
    /// Builds a provider carrying the production authentication + authorization
    /// registrations, so both the configured <see cref="JwtBearerOptions"/> and the
    /// named policies come from the exact production setup.
    /// </summary>
    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJwtAuthenticationAndPolicies(configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Validates <paramref name="token"/> through the configured bearer options exactly
    /// as the authentication middleware would, returning the resulting principal. Honors
    /// <see cref="JwtBearerOptions.MapInboundClaims"/> so the claim names seen by the
    /// policies match runtime behavior.
    /// </summary>
    private static ClaimsPrincipal ValidateThroughConfiguredHandler(
        ServiceProvider provider,
        string token)
    {
        var bearerOptions = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        // Use the same handler family the bearer middleware uses, with its inbound-claim
        // mapping driven by the configured option. This is what causes the "role" claim
        // to be remapped (or not), which is the crux of the bug.
        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = bearerOptions.MapInboundClaims,
        };

        return handler.ValidateToken(token, bearerOptions.TokenValidationParameters, out _);
    }

    private static string IssueAccessToken(IConfiguration configuration, Role role)
    {
        var generator = new JwtTokenGenerator(configuration);
        var person = new Person
        {
            Id = Guid.NewGuid(),
            Name = "Integration Test",
            Email = "person@example.com",
            PasswordHash = "irrelevant-for-token-issuance",
            Role = role,
            CreatedAt = DateTime.UtcNow,
        };

        return generator.CreateAccessToken(person);
    }

    private static async Task<bool> IsAuthorizedAsync(
        ServiceProvider provider,
        ClaimsPrincipal principal,
        string policy)
    {
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        var result = await authorizationService.AuthorizeAsync(principal, resource: null, policy);
        return result.Succeeded;
    }

    /// <summary>
    /// The crux of the bug: an Admin token, after passing through the configured
    /// validation (with inbound claim mapping disabled), satisfies MovieAccess. This
    /// FAILS before the fix (role claim gets remapped to its long URI, so the policy's
    /// FindFirst("role") is null) and PASSES after it (R4.3, R4.6).
    /// </summary>
    [Fact]
    public async Task Admin_token_through_real_validation_satisfies_MovieAccess()
    {
        var configuration = BuildConfiguration();
        using var provider = BuildProvider(configuration);

        var token = IssueAccessToken(configuration, Role.Admin);
        var principal = ValidateThroughConfiguredHandler(provider, token);

        Assert.True(
            await IsAuthorizedAsync(provider, principal, AuthenticationSetup.MovieAccessPolicy),
            "An authenticated Admin must be authorized for MovieAccess through the real validation path.");
    }

    /// <summary>
    /// Admin passes all three policies when the role claim resolves through the real
    /// validation path (R4.6).
    /// </summary>
    [Fact]
    public async Task Admin_token_through_real_validation_satisfies_all_policies()
    {
        var configuration = BuildConfiguration();
        using var provider = BuildProvider(configuration);

        var token = IssueAccessToken(configuration, Role.Admin);
        var principal = ValidateThroughConfiguredHandler(provider, token);

        Assert.True(await IsAuthorizedAsync(provider, principal, AuthenticationSetup.MovieAccessPolicy));
        Assert.True(await IsAuthorizedAsync(provider, principal, AuthenticationSetup.AlbumAccessPolicy));
        Assert.True(await IsAuthorizedAsync(provider, principal, AuthenticationSetup.AdminOnlyPolicy));
    }

    /// <summary>
    /// A User_Movie token resolves its role through the real validation path: authorized
    /// for MovieAccess, forbidden for AdminOnly (R4.3, R4.7). This confirms the claim
    /// genuinely flows through validation rather than being read from a literal claim.
    /// </summary>
    [Fact]
    public async Task UserMovie_token_through_real_validation_permits_movie_and_forbids_admin()
    {
        var configuration = BuildConfiguration();
        using var provider = BuildProvider(configuration);

        var token = IssueAccessToken(configuration, Role.User_Movie);
        var principal = ValidateThroughConfiguredHandler(provider, token);

        Assert.True(
            await IsAuthorizedAsync(provider, principal, AuthenticationSetup.MovieAccessPolicy),
            "User_Movie must be authorized for MovieAccess.");
        Assert.False(
            await IsAuthorizedAsync(provider, principal, AuthenticationSetup.AdminOnlyPolicy),
            "User_Movie must be forbidden for AdminOnly.");
    }

    /// <summary>
    /// Guards against silent regression of the fix: the configured bearer handler must
    /// NOT remap inbound claims, otherwise the <c>role</c> claim resolves under its long
    /// WS-* URI and the policies break.
    /// </summary>
    [Fact]
    public void Configured_bearer_handler_does_not_remap_inbound_claims()
    {
        var configuration = BuildConfiguration();
        using var provider = BuildProvider(configuration);

        var bearerOptions = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.False(
            bearerOptions.MapInboundClaims,
            "Inbound claim mapping must stay disabled so the 'role' claim keeps its original name.");

        var token = IssueAccessToken(configuration, Role.Admin);
        var principal = ValidateThroughConfiguredHandler(provider, token);

        Assert.Equal(
            Role.Admin.ToString(),
            principal.FindFirst(AuthenticationSetup.RoleClaimType)?.Value);
    }
}
