using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StreamingPanel.Api.Extensions;
using StreamingPanel.Core.Enums;

namespace StreamingPanel.Tests.Authorization;

/// <summary>
/// Example (xUnit) tests for the Authorization_Component role matrix.
///
/// Feature: streaming-panel, Authorization_Component role matrix example tests
/// Validates: Requirements 23.2, 23.3, 23.4, 23.5
///
/// <para>
/// These example tests complement the property tests in task 5.2. They enumerate
/// the full role/resource matrix from Requirement 4 and assert, for every
/// <see cref="Role"/>, that each guarded resource (Movie catalog, Album catalog,
/// admin/write) is permitted when authorized and denied when not. A denied policy
/// result for an authenticated caller corresponds to a 403 Forbidden response
/// (R4.2, R23.2).
/// </para>
///
/// <para>
/// The policies are evaluated directly via <see cref="IAuthorizationService"/> over
/// the exact named policies registered by
/// <see cref="AuthenticationSetup.AddJwtAuthenticationAndPolicies"/>, avoiding any
/// live database or application startup (migrate/seed).
/// </para>
/// </summary>
public class AuthorizationMatrixTests
{
    // Minimal valid Jwt configuration so the policy registration succeeds; the
    // signing/issuer/audience values are irrelevant to policy evaluation itself.
    private const string SigningKey = "streaming-panel-test-signing-key-0123456789";
    private const string Issuer = "streaming-panel-tests";
    private const string Audience = "streaming-panel-clients";

    /// <summary>
    /// Builds an <see cref="IAuthorizationService"/> backed by the real named
    /// policies (<c>MovieAccess</c>, <c>AlbumAccess</c>, <c>AdminOnly</c>)
    /// registered by the API's authentication setup.
    /// </summary>
    private static IAuthorizationService CreateAuthorizationService()
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

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    /// <summary>
    /// Builds an authenticated <see cref="ClaimsPrincipal"/> carrying the lowercase
    /// <c>role</c> claim the policies assert over, mirroring the token generator.
    /// </summary>
    private static ClaimsPrincipal CreatePrincipal(Role role)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(AuthenticationSetup.RoleClaimType, role.ToString()) },
            authenticationType: "TestAuth");

        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> IsPermittedAsync(Role role, string policy)
    {
        var authorizationService = CreateAuthorizationService();
        var principal = CreatePrincipal(role);

        var result = await authorizationService.AuthorizeAsync(principal, resource: null, policy);
        return result.Succeeded;
    }

    // The complete role/resource authorization matrix from Requirement 4.
    // expectedPermitted == true  -> policy must permit the role.
    // expectedPermitted == false -> policy must deny the role (403 for an
    //                               authenticated caller, R23.2).
    public static IEnumerable<object[]> MatrixCases()
    {
        // R23.3 / R4.3 - User_Movie: Movie allowed; Album denied; Admin denied.
        yield return new object[] { Role.User_Movie, AuthenticationSetup.MovieAccessPolicy, true };
        yield return new object[] { Role.User_Movie, AuthenticationSetup.AlbumAccessPolicy, false };
        yield return new object[] { Role.User_Movie, AuthenticationSetup.AdminOnlyPolicy, false };

        // R23.4 / R4.4 - User_Album: Album allowed; Movie denied; Admin denied.
        yield return new object[] { Role.User_Album, AuthenticationSetup.AlbumAccessPolicy, true };
        yield return new object[] { Role.User_Album, AuthenticationSetup.MovieAccessPolicy, false };
        yield return new object[] { Role.User_Album, AuthenticationSetup.AdminOnlyPolicy, false };

        // R23.5 / R4.5 - User_Full: Movie + Album allowed; Admin denied.
        yield return new object[] { Role.User_Full, AuthenticationSetup.MovieAccessPolicy, true };
        yield return new object[] { Role.User_Full, AuthenticationSetup.AlbumAccessPolicy, true };
        yield return new object[] { Role.User_Full, AuthenticationSetup.AdminOnlyPolicy, false };

        // R4.6 - Admin: everything allowed.
        yield return new object[] { Role.Admin, AuthenticationSetup.MovieAccessPolicy, true };
        yield return new object[] { Role.Admin, AuthenticationSetup.AlbumAccessPolicy, true };
        yield return new object[] { Role.Admin, AuthenticationSetup.AdminOnlyPolicy, true };
    }

    /// <summary>
    /// For every role, asserts permit for authorized resources and deny for
    /// unauthorized resources across the Movie catalog, Album catalog, and
    /// admin/write policies (R23.2–R23.5).
    /// </summary>
    [Theory]
    [MemberData(nameof(MatrixCases))]
    public async Task Policy_matches_the_authorization_matrix_for_role(
        Role role,
        string policy,
        bool expectedPermitted)
    {
        var permitted = await IsPermittedAsync(role, policy);

        Assert.Equal(expectedPermitted, permitted);
    }

    // --- Explicit per-role facts (readable, individually-reported cases, R23.4) ---

    [Fact]
    public async Task UserMovie_permits_movie_and_denies_album_and_admin()
    {
        Assert.True(await IsPermittedAsync(Role.User_Movie, AuthenticationSetup.MovieAccessPolicy));
        Assert.False(await IsPermittedAsync(Role.User_Movie, AuthenticationSetup.AlbumAccessPolicy));
        Assert.False(await IsPermittedAsync(Role.User_Movie, AuthenticationSetup.AdminOnlyPolicy));
    }

    [Fact]
    public async Task UserAlbum_permits_album_and_denies_movie_and_admin()
    {
        Assert.True(await IsPermittedAsync(Role.User_Album, AuthenticationSetup.AlbumAccessPolicy));
        Assert.False(await IsPermittedAsync(Role.User_Album, AuthenticationSetup.MovieAccessPolicy));
        Assert.False(await IsPermittedAsync(Role.User_Album, AuthenticationSetup.AdminOnlyPolicy));
    }

    [Fact]
    public async Task UserFull_permits_movie_and_album_and_denies_admin()
    {
        Assert.True(await IsPermittedAsync(Role.User_Full, AuthenticationSetup.MovieAccessPolicy));
        Assert.True(await IsPermittedAsync(Role.User_Full, AuthenticationSetup.AlbumAccessPolicy));
        Assert.False(await IsPermittedAsync(Role.User_Full, AuthenticationSetup.AdminOnlyPolicy));
    }

    [Fact]
    public async Task Admin_permits_movie_and_album_and_admin()
    {
        Assert.True(await IsPermittedAsync(Role.Admin, AuthenticationSetup.MovieAccessPolicy));
        Assert.True(await IsPermittedAsync(Role.Admin, AuthenticationSetup.AlbumAccessPolicy));
        Assert.True(await IsPermittedAsync(Role.Admin, AuthenticationSetup.AdminOnlyPolicy));
    }
}
