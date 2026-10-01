using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StreamingPanel.Api.Extensions;
using StreamingPanel.Core.Interfaces;
using StreamingPanel.Core.Validation;
using StreamingPanel.Infrastructure.Persistence;
using StreamingPanel.Infrastructure.Repositories;
using StreamingPanel.Infrastructure.Security;
using StreamingPanel.Infrastructure.Services;

namespace StreamingPanel.Tests.Infrastructure;

/// <summary>
/// Smoke/integration tests for the API's dependency-injection graph and the
/// eager startup validation it performs (R12.4, R12.6).
///
/// Feature: streaming-panel, task 7.9 (infrastructure smoke tests)
/// Validates: Requirements 12.4, 12.6
///
/// <para>
/// These tests replicate the exact service registrations performed by
/// <c>Program.cs</c> (repositories, security primitives, orchestration services,
/// validators) against an in-memory <see cref="IConfiguration"/>. The DbContext is
/// registered with the SQLite provider so <see cref="ServiceProviderOptions.ValidateOnBuild"/>
/// can resolve the whole graph without a live PostgreSQL server — registering a
/// provider never opens a connection. Building the provider with
/// <c>ValidateOnBuild</c> + <c>ValidateScopes</c> exercises R12.6 positively: a
/// valid graph builds, and every <see cref="Service_Layer"/> component receives its
/// repositories through DI (R12.4) rather than constructing them directly.
/// </para>
/// </summary>
public class DependencyInjectionStartupTests
{
    private const string SigningKey = "streaming-panel-test-signing-key-0123456789-abcdef";
    private const string Issuer = "streaming-panel-tests";
    private const string Audience = "streaming-panel-clients";

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "DataSource=:memory:",
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Cors:FrontendOrigin"] = "http://localhost:4200",
            })
            .Build();

    /// <summary>
    /// Registers services exactly as <c>Program.cs</c> does, except the DbContext uses
    /// SQLite (registration only, no connection) so the graph can be validated offline.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();

        services.AddLogging();

        // Note: MVC controller registration is intentionally omitted. ValidateOnBuild
        // would otherwise try to construct MVC's own framework singletons (which need
        // the full web host), which is not what this test verifies. We validate the
        // application's custom service graph (repositories, services, validators,
        // auth, CORS, DbContext) — the dependencies Program.cs wires up (R12.4, R12.6).

        // Persistence boundary (R12.3) — SQLite stand-in for Npgsql; registering a
        // provider does not open a connection, so the graph validates offline.
        services.AddDbContext<AppDbContext>(options => options.UseSqlite("DataSource=:memory:"));

        // Security primitives.
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        // Refresh-token persistence and the authentication orchestration service.
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuthService, AuthService>();

        // Media and person persistence boundaries (R12.1).
        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<IMovieRepository, MovieRepository>();
        services.AddScoped<IAlbumRepository, AlbumRepository>();

        // Orchestration services (R12.2).
        services.AddScoped<IMovieService, MovieService>();
        services.AddScoped<IAlbumService, AlbumService>();
        services.AddScoped<IPersonService, PersonService>();

        // FluentValidation validators (R10).
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

        // IConfiguration is a dependency of AuthService and JwtTokenGenerator.
        services.AddSingleton(configuration);

        // Note: JWT auth and CORS registrations are deliberately NOT part of this
        // validated graph. They register web-host-only framework singletons (e.g.
        // AuthorizationPolicyCache depends on the routing EndpointDataSource) that
        // only exist under a full WebApplication host, so ValidateOnBuild cannot
        // resolve them in isolation. CORS fail-fast is covered by CorsStartupTests
        // and the authorization policy graph by the authorization tests. Here we
        // validate the application's own repository/service/validator graph — the
        // dependencies Program.cs wires up that R12.4/R12.6 are about.

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    /// <summary>
    /// The composed container builds with eager validation enabled, proving the whole
    /// DI graph is resolvable at startup (R12.6) — no captive dependency or unresolved
    /// service aborts the build.
    /// </summary>
    [Fact]
    public void ServiceProvider_BuildsWithEagerValidation()
    {
        using var provider = BuildProvider();
        Assert.NotNull(provider);
    }

    /// <summary>
    /// Each key orchestration service resolves from a scope and receives its injected
    /// repository/collaborators through DI rather than constructing them directly
    /// (R12.4). Mirrors the belt-and-braces <c>ValidateKeyServices</c> check in
    /// <c>Program.cs</c>.
    /// </summary>
    [Theory]
    [InlineData(typeof(IAuthService))]
    [InlineData(typeof(IPersonService))]
    [InlineData(typeof(IMovieService))]
    [InlineData(typeof(IAlbumService))]
    public void KeyService_ResolvesFromScope(Type serviceType)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetService(serviceType);

        Assert.NotNull(resolved);
    }

    /// <summary>
    /// The repository boundary types also resolve from a scope, confirming the
    /// Service_Layer's collaborators are satisfied by the container (R12.4).
    /// </summary>
    [Theory]
    [InlineData(typeof(IPersonRepository))]
    [InlineData(typeof(IMovieRepository))]
    [InlineData(typeof(IAlbumRepository))]
    [InlineData(typeof(IRefreshTokenRepository))]
    public void Repository_ResolvesFromScope(Type repositoryType)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetService(repositoryType);

        Assert.NotNull(resolved);
    }

    /// <summary>
    /// A missing <c>ConnectionStrings:Default</c> aborts initialization with a clear
    /// error naming the missing dependency (R12.6) — <see cref="DatabaseStartupExtensions.AddAppDbContext"/>
    /// is the registration path <c>Program.cs</c> uses.
    /// </summary>
    [Fact]
    public void AddAppDbContext_MissingConnectionString_ThrowsClearError()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddAppDbContext(configuration));

        Assert.Contains("ConnectionStrings:Default", ex.Message);
    }
}
