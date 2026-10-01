using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Enums;
using StreamingPanel.Core.Interfaces;
using StreamingPanel.Infrastructure.Persistence;
using StreamingPanel.Infrastructure.Repositories;
using StreamingPanel.Infrastructure.Security;
using StreamingPanel.Infrastructure.Services;

namespace StreamingPanel.Tests.Security;

/// <summary>
/// Example (by-example) xUnit tests for <see cref="AuthService"/> authentication logic
/// mandated by Requirement 23: valid credential acceptance, invalid credential rejection,
/// expired/malformed/unknown refresh-token rejection, the 2-second login/refresh latency
/// budgets (R2.1, R3.1), and the database-unavailable → 503 mapping (R2.7).
///
/// These complement the property-based coverage in
/// <see cref="AuthServiceRegistrationLoginPropertyTests"/>: here each [Fact] pins a single
/// concrete scenario so a failing case reports an explicit, isolated pass/fail (R23.4/R23.5).
///
/// Database provider: Microsoft.EntityFrameworkCore.Sqlite (in-memory), reusing the
/// <see cref="SqliteAuthDbContext"/> harness so the real <see cref="AppDbContext"/>,
/// <see cref="PasswordHasher"/>, <see cref="JwtTokenGenerator"/>, and
/// <see cref="RefreshTokenRepository"/> run unchanged against a relational store.
///
/// Database-unavailable simulation: <see cref="AuthService.LoginAsync"/> reads the Person
/// via the <see cref="AppDbContext"/> directly but issues the token pair through
/// <see cref="IRefreshTokenRepository"/>. Closing/disposing the SQLite connection would throw
/// a <c>SqliteException</c>, which <c>AuthService.IsDatabaseUnavailable</c> does NOT classify
/// as an outage. Instead we inject <see cref="ThrowingRefreshTokenRepository"/>, a hand-rolled
/// stub (Moq is not referenced by this test project) whose persistence calls throw a
/// <see cref="TimeoutException"/> — one of the exception shapes <c>IsDatabaseUnavailable</c>
/// treats as a database outage — so the service maps the failure to
/// <see cref="ErrorCode.Unavailable"/> (503) and issues no token (R2.7).
///
/// Validates: Requirements 2.1, 2.7, 3.1, 23.1, 23.4, 23.5
/// </summary>
public class AuthServiceExampleTests
{
    private const string Email = "ada@lovelace.dev";
    private const string Password = "correct horse battery";
    private static readonly string Role = nameof(StreamingPanel.Core.Enums.Role.Admin);

    /// <summary>
    /// R2.1 / R23.1 — Valid credentials are accepted: after registering a Person, logging in
    /// with the correct email + password returns a success Result carrying a non-empty access
    /// token and a non-empty refresh token, with the Role echoed back.
    /// </summary>
    [Fact]
    public void LoginAsync_WithValidCredentials_ReturnsTokenPair()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        RegisterPerson(options, Email, Password, Role);

        Result<AuthResponse> login;
        using (var ctx = new SqliteAuthDbContext(options))
        {
            login = BuildService(ctx)
                .LoginAsync(new LoginRequest(Email, Password))
                .GetAwaiter().GetResult();
        }

        Assert.True(login.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(login.Value.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(login.Value.RefreshToken));
        Assert.Equal(Role, login.Value.Role);
    }

    /// <summary>
    /// R23.4 / R23.5 — Invalid credentials are rejected: logging in with the wrong password for
    /// an existing Person returns a 401 Unauthorized failure, exposes no value, and persists no
    /// refresh token.
    /// </summary>
    [Fact]
    public void LoginAsync_WithWrongPassword_ReturnsUnauthorizedAndIssuesNoToken()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        RegisterPerson(options, Email, Password, Role);

        int refreshBefore;
        using (var ctx = new SqliteAuthDbContext(options))
        {
            refreshBefore = ctx.RefreshTokens.Count();
        }

        Result<AuthResponse> login;
        using (var ctx = new SqliteAuthDbContext(options))
        {
            login = BuildService(ctx)
                .LoginAsync(new LoginRequest(Email, Password + "-wrong"))
                .GetAwaiter().GetResult();
        }

        using var verify = new SqliteAuthDbContext(options);
        var refreshAfter = verify.RefreshTokens.Count();

        Assert.True(login.IsFailure);
        Assert.Equal(ErrorCode.Unauthorized, login.ErrorCode);
        Assert.Equal(refreshBefore, refreshAfter);
    }

    /// <summary>
    /// R3.1 — A valid, non-expired refresh token is rotated into a brand-new token pair: the
    /// returned pair's refresh token differs from the one presented, and the old token is
    /// invalidated (its re-presentation past the grace path yields 401, covered separately).
    /// </summary>
    [Fact]
    public void RefreshAsync_WithValidToken_ReturnsNewPair()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        var initial = RegisterPerson(options, Email, Password, Role);

        Result<AuthResponse> refreshed;
        using (var ctx = new SqliteAuthDbContext(options))
        {
            refreshed = BuildService(ctx)
                .RefreshAsync(initial.RefreshToken)
                .GetAwaiter().GetResult();
        }

        Assert.True(refreshed.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.Value.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshed.Value.RefreshToken));
        Assert.NotEqual(initial.RefreshToken, refreshed.Value.RefreshToken);
    }

    /// <summary>
    /// R3.1-adjacent (rejection path) — A malformed/unknown refresh token (one that was never
    /// issued) hashes to a value absent from the store, so refresh returns 401 Unauthorized and
    /// issues no new pair.
    /// </summary>
    [Fact]
    public void RefreshAsync_WithUnknownToken_ReturnsUnauthorized()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        RegisterPerson(options, Email, Password, Role);

        Result<AuthResponse> refreshed;
        using (var ctx = new SqliteAuthDbContext(options))
        {
            refreshed = BuildService(ctx)
                .RefreshAsync("this-token-was-never-issued")
                .GetAwaiter().GetResult();
        }

        Assert.True(refreshed.IsFailure);
        Assert.Equal(ErrorCode.Unauthorized, refreshed.ErrorCode);
    }

    /// <summary>
    /// R3.2 (expired path) — A refresh token whose stored row has already expired is rejected
    /// with 401 and issues no new pair. We seed an expired token row directly (hash of a known
    /// raw value) so the service's lookup finds it but classifies it inactive/expired.
    /// </summary>
    [Fact]
    public void RefreshAsync_WithExpiredToken_ReturnsUnauthorized()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        var person = SeedPersonEntity(options, Email, Password, StreamingPanel.Core.Enums.Role.Admin);

        const string rawExpired = "expired-raw-refresh-token-value";
        using (var ctx = new SqliteAuthDbContext(options))
        {
            ctx.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                PersonId = person.Id,
                TokenHash = JwtTokenGenerator.HashToken(rawExpired),
                CreatedAt = DateTime.UtcNow.AddDays(-8),
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1), // already expired
            });
            ctx.SaveChanges();
        }

        Result<AuthResponse> refreshed;
        using (var ctx = new SqliteAuthDbContext(options))
        {
            refreshed = BuildService(ctx)
                .RefreshAsync(rawExpired)
                .GetAwaiter().GetResult();
        }

        Assert.True(refreshed.IsFailure);
        Assert.Equal(ErrorCode.Unauthorized, refreshed.ErrorCode);
    }

    /// <summary>
    /// R2.1 — Login completes well within its 2-second budget. The budget is generous for the
    /// in-memory SQLite harness; we measure wall-clock with a <see cref="Stopwatch"/> and assert
    /// the elapsed time is under 2000 ms.
    /// </summary>
    [Fact]
    public void LoginAsync_CompletesWithinTwoSecondBudget()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        RegisterPerson(options, Email, Password, Role);

        using var ctx = new SqliteAuthDbContext(options);
        var service = BuildService(ctx);

        var sw = Stopwatch.StartNew();
        var login = service.LoginAsync(new LoginRequest(Email, Password)).GetAwaiter().GetResult();
        sw.Stop();

        Assert.True(login.IsSuccess);
        Assert.True(
            sw.ElapsedMilliseconds < 2000,
            $"LoginAsync took {sw.ElapsedMilliseconds} ms, exceeding the 2000 ms budget (R2.1).");
    }

    /// <summary>
    /// R3.1 — Refresh completes well within its 2000-millisecond budget, measured with a
    /// <see cref="Stopwatch"/> around a single valid rotation.
    /// </summary>
    [Fact]
    public void RefreshAsync_CompletesWithinTwoSecondBudget()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        var initial = RegisterPerson(options, Email, Password, Role);

        using var ctx = new SqliteAuthDbContext(options);
        var service = BuildService(ctx);

        var sw = Stopwatch.StartNew();
        var refreshed = service.RefreshAsync(initial.RefreshToken).GetAwaiter().GetResult();
        sw.Stop();

        Assert.True(refreshed.IsSuccess);
        Assert.True(
            sw.ElapsedMilliseconds < 2000,
            $"RefreshAsync took {sw.ElapsedMilliseconds} ms, exceeding the 2000 ms budget (R3.1).");
    }

    /// <summary>
    /// R2.7 — When the database is unavailable during credential verification, login returns a
    /// 503 Unavailable failure and issues no token. We inject a repository whose persistence
    /// throws a <see cref="TimeoutException"/> (classified as an outage by
    /// <c>AuthService.IsDatabaseUnavailable</c>) on the token-issuance step of a successful login.
    /// </summary>
    [Fact]
    public void LoginAsync_WhenDatabaseUnavailable_ReturnsUnavailable()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        // Seed a real, resolvable Person so credential verification SUCCEEDS and the service
        // proceeds to persist a refresh token — the point at which the outage surfaces.
        SeedPersonEntity(options, Email, Password, StreamingPanel.Core.Enums.Role.Admin);

        Result<AuthResponse> login;
        using (var ctx = new SqliteAuthDbContext(options))
        {
            var service = BuildServiceWithRepository(
                ctx, new ThrowingRefreshTokenRepository(() => new TimeoutException("DB connection timed out.")));
            login = service.LoginAsync(new LoginRequest(Email, Password)).GetAwaiter().GetResult();
        }

        Assert.True(login.IsFailure);
        Assert.Equal(ErrorCode.Unavailable, login.ErrorCode);
    }

    /// <summary>
    /// R2.7 (Npgsql shape) — The outage mapping also fires for a transient
    /// <see cref="NpgsqlException"/> thrown from persistence, confirming the service classifies
    /// the production database exception type as 503 rather than letting it escape.
    /// </summary>
    [Fact]
    public void LoginAsync_WhenNpgsqlFailureDuringPersist_ReturnsUnavailable()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        SeedPersonEntity(options, Email, Password, StreamingPanel.Core.Enums.Role.Admin);

        Result<AuthResponse> login;
        using (var ctx = new SqliteAuthDbContext(options))
        {
            var service = BuildServiceWithRepository(
                ctx, new ThrowingRefreshTokenRepository(() => new NpgsqlException("connection refused")));
            login = service.LoginAsync(new LoginRequest(Email, Password)).GetAwaiter().GetResult();
        }

        Assert.True(login.IsFailure);
        Assert.Equal(ErrorCode.Unavailable, login.ErrorCode);
    }

    // ---- Shared test harness -------------------------------------------------

    private static SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = BuildOptions(connection);
        using var ctx = new SqliteAuthDbContext(options);
        ctx.Database.EnsureCreated();

        return connection;
    }

    private static DbContextOptions<AppDbContext> BuildOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "test-signing-key-at-least-32-bytes-long-0123456789",
                ["Jwt:Issuer"] = "streaming-panel-tests",
                ["Jwt:Audience"] = "streaming-panel-tests",
                ["Jwt:RefreshGraceSeconds"] = "8",
            })
            .Build();

    private static AuthService BuildService(AppDbContext ctx)
    {
        var configuration = BuildConfiguration();
        return new AuthService(
            ctx,
            new PasswordHasher(),
            new JwtTokenGenerator(configuration),
            new RefreshTokenRepository(ctx),
            configuration);
    }

    private static AuthService BuildServiceWithRepository(AppDbContext ctx, IRefreshTokenRepository refreshTokens)
    {
        var configuration = BuildConfiguration();
        return new AuthService(
            ctx,
            new PasswordHasher(),
            new JwtTokenGenerator(configuration),
            refreshTokens,
            configuration);
    }

    /// <summary>Registers a Person through the real service and returns the issued pair.</summary>
    private static AuthResponse RegisterPerson(
        DbContextOptions<AppDbContext> options, string email, string password, string role)
    {
        using var ctx = new SqliteAuthDbContext(options);
        var result = BuildService(ctx)
            .RegisterAsync(new RegisterRequest(email, password, role, null))
            .GetAwaiter().GetResult();
        Assert.True(result.IsSuccess, "Precondition: registration should succeed to set up the test.");
        return result.Value;
    }

    /// <summary>
    /// Seeds a Person row directly (hashing the password with the production hasher) without
    /// issuing any refresh token, so login against it can be driven through a stubbed repository.
    /// </summary>
    private static Person SeedPersonEntity(
        DbContextOptions<AppDbContext> options, string email, string password, StreamingPanel.Core.Enums.Role role)
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            Name = "Seed",
            Email = email,
            PasswordHash = new PasswordHasher().Hash(password),
            Role = role,
            CreatedAt = DateTime.UtcNow,
        };

        using var ctx = new SqliteAuthDbContext(options);
        ctx.Persons.Add(person);
        ctx.SaveChanges();
        return person;
    }

    /// <summary>
    /// Hand-rolled <see cref="IRefreshTokenRepository"/> stub whose write path throws a
    /// caller-supplied exception, used to simulate a database outage during the token-issuance
    /// step. Lookups return <c>null</c> (no stored tokens in these scenarios).
    /// </summary>
    private sealed class ThrowingRefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly Func<Exception> _exceptionFactory;

        public ThrowingRefreshTokenRepository(Func<Exception> exceptionFactory) =>
            _exceptionFactory = exceptionFactory;

        public Task<RefreshToken?> GetActiveAsync(string tokenHash) =>
            Task.FromResult<RefreshToken?>(null);

        public Task AddAsync(RefreshToken token) => throw _exceptionFactory();

        public Task InvalidateAsync(RefreshToken token) => throw _exceptionFactory();

        public Task SaveChangesAsync() => throw _exceptionFactory();
    }
}
