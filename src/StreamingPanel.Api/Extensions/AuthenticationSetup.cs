using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using StreamingPanel.Core.Enums;

namespace StreamingPanel.Api.Extensions;

/// <summary>
/// Registers JWT bearer authentication and the named role-based authorization policies.
/// Access tokens carry the caller's role in a lowercase <c>role</c> claim and are validated
/// for issuer, audience, signing key, and lifetime from <c>Jwt:*</c> configuration.
/// <para>
/// Policy to role mapping:
/// <list type="bullet">
/// <item><description><c>MovieAccess</c>: User_Movie, User_Full, Admin.</description></item>
/// <item><description><c>AlbumAccess</c>: User_Album, User_Full, Admin.</description></item>
/// <item><description><c>AdminOnly</c>: Admin.</description></item>
/// </list>
/// Policies are written as assertions over the <c>role</c> claim, so an authenticated caller
/// with an insufficient role fails the policy (403) rather than being challenged again (401).
/// </para>
/// </summary>
public static class AuthenticationSetup
{
    /// <summary>Policy guarding Movie catalog reads.</summary>
    public const string MovieAccessPolicy = "MovieAccess";

    /// <summary>Policy guarding Album catalog reads.</summary>
    public const string AlbumAccessPolicy = "AlbumAccess";

    /// <summary>Policy guarding Person CRUD, media writes, and admin endpoints.</summary>
    public const string AdminOnlyPolicy = "AdminOnly";

    /// <summary>The claim type carrying the caller's role, as emitted by the token generator.</summary>
    public const string RoleClaimType = "role";

    /// <summary>
    /// Registers JWT bearer authentication and the named authorization policies
    /// (<see cref="MovieAccessPolicy"/>, <see cref="AlbumAccessPolicy"/>,
    /// <see cref="AdminOnlyPolicy"/>) on the service collection.
    /// </summary>
    public static IServiceCollection AddJwtAuthenticationAndPolicies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Missing required configuration value 'Jwt:SigningKey'.");
        var issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Missing required configuration value 'Jwt:Issuer'.");
        var audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Missing required configuration value 'Jwt:Audience'.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep inbound claims verbatim. By default the handler maps "role" to a long
                // WS-* URI, so FindFirst("role") returns null and every authenticated caller is
                // forbidden (403). Disabling the mapping lets "role" arrive under its own name.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidateLifetime = true,
                    // Match the lowercase "role" claim emitted by the token generator.
                    RoleClaimType = RoleClaimType,
                    // No leeway on expiry: an expired access token is rejected.
                    ClockSkew = TimeSpan.Zero,
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(MovieAccessPolicy, policy =>
                policy.RequireAssertion(context => HasAnyRole(
                    context,
                    Role.User_Movie,
                    Role.User_Full,
                    Role.Admin)));

            options.AddPolicy(AlbumAccessPolicy, policy =>
                policy.RequireAssertion(context => HasAnyRole(
                    context,
                    Role.User_Album,
                    Role.User_Full,
                    Role.Admin)));

            options.AddPolicy(AdminOnlyPolicy, policy =>
                policy.RequireAssertion(context => HasAnyRole(
                    context,
                    Role.Admin)));
        });

        return services;
    }

    /// <summary>
    /// Returns true when the caller's <c>role</c> claim exactly matches one of the permitted
    /// roles. An absent or unrecognized role never matches, so the caller is forbidden (403).
    /// </summary>
    private static bool HasAnyRole(
        Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext context,
        params Role[] permitted)
    {
        var roleValue = context.User.FindFirst(RoleClaimType)?.Value;
        if (string.IsNullOrEmpty(roleValue))
        {
            return false;
        }

        foreach (var role in permitted)
        {
            if (string.Equals(roleValue, role.ToString(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
