using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Enums;
using StreamingPanel.Infrastructure.Persistence;
using StreamingPanel.Infrastructure.Repositories;
using StreamingPanel.Infrastructure.Security;
using StreamingPanel.Infrastructure.Services;

namespace StreamingPanel.Tests.Security;

/// <summary>
/// Property-based tests for <see cref="AuthService"/> registration and login.
///
/// Feature: streaming-panel, Property 2: Registration persists hashed credentials for valid, unused emails
/// Feature: streaming-panel, Property 3: Duplicate-email registration is rejected and persists nothing
/// Feature: streaming-panel, Property 6: Login rejects non-matching and malformed credentials without issuing tokens
///
/// Validates: Requirements 1.1, 1.3, 1.5, 2.5, 2.6
///
/// Database provider: Microsoft.EntityFrameworkCore.Sqlite (in-memory). SQLite is used
/// instead of the EF Core InMemory provider so the real <see cref="AppDbContext"/>,
/// entity configurations, <see cref="PasswordHasher"/>, <see cref="JwtTokenGenerator"/>,
/// and <see cref="RefreshTokenRepository"/> are exercised unchanged against a relational
/// store. The production <see cref="Movie"/> mapping stores <c>MainActors</c> as a
/// PostgreSQL <c>text[]</c>, which SQLite cannot represent natively; the test-only
/// <see cref="SqliteAuthDbContext"/> supplies a JSON value converter for that one
/// property (none of the Person/RefreshToken mappings exercised here are affected).
/// Each sample runs against a fresh in-memory database for isolation; every property
/// executes a minimum of 100 iterations.
/// </summary>
public class AuthServiceRegistrationLoginPropertyTests
{
    // The real roles accepted by registration (R1.1, R1.6).
    private static readonly string[] ValidRoles =
    {
        nameof(Role.Admin),
        nameof(Role.User_Movie),
        nameof(Role.User_Album),
        nameof(Role.User_Full),
    };

    /// <summary>Local-part + domain generator producing standard-format, in-range emails (R1.1).</summary>
    private static readonly Gen<string> GenEmail =
        from local in Gen.Char['a', 'z'].Array[1, 20]
        from domain in Gen.Char['a', 'z'].Array[1, 15]
        from tld in Gen.OneOfConst("com", "org", "net", "io", "dev")
        select $"{new string(local)}@{new string(domain)}.{tld}";

    /// <summary>Passwords within the valid 8–128 character range (R1.1).</summary>
    private static readonly Gen<string> GenPassword =
        Gen.Char[' ', '~'].Array[8, 40].Select(cs => new string(cs));

    private static readonly Gen<string> GenRole = Gen.OneOfConst(ValidRoles);

    private static readonly Gen<string?> GenName =
        Gen.Char['a', 'z'].Array[0, 20].Select(cs => cs.Length == 0 ? null : new string(cs));

    /// <summary>
    /// Property 2 — Registration persists hashed credentials for valid, unused emails.
    ///
    /// For any valid, unused email + in-range password + known role, <see cref="AuthService.RegisterAsync"/>
    /// succeeds, a single Person row is persisted whose stored hash is neither equal to nor contains
    /// the plaintext yet verifies against it, and whose CreatedAt is a UTC timestamp near "now".
    ///
    /// Feature: streaming-panel, Property 2: Registration persists hashed credentials for valid, unused emails
    /// Validates: Requirements 1.1, 1.5
    /// </summary>
    [Fact]
    public void RegistrationPersistsHashedCredentialsForValidUnusedEmails()
    {
        (from email in GenEmail
         from password in GenPassword
         from role in GenRole
         from name in GenName
         select (email, password, role, name))
            .Sample(
                input =>
                {
                    var (email, password, role, name) = input;

                    using var connection = OpenDatabase();
                    var options = BuildOptions(connection);
                    var before = DateTime.UtcNow;

                    Result<AuthResponse> result;
                    using (var ctx = new SqliteAuthDbContext(options))
                    {
                        var service = BuildService(ctx);
                        result = service
                            .RegisterAsync(new RegisterRequest(email, password, role, name))
                            .GetAwaiter().GetResult();
                    }

                    var after = DateTime.UtcNow;

                    if (!result.IsSuccess)
                    {
                        return false;
                    }

                    using var verify = new SqliteAuthDbContext(options);
                    var persons = verify.Persons.Where(p => p.Email == email).ToList();
                    if (persons.Count != 1)
                    {
                        return false;
                    }

                    var person = persons[0];
                    var hasher = new PasswordHasher();

                    var hashIsNotPlaintext = person.PasswordHash != password;
                    var hashDoesNotEmbedPlaintext =
                        !person.PasswordHash.Contains(password, StringComparison.Ordinal);
                    var hashVerifies = hasher.Verify(password, person.PasswordHash);
                    var roleMatches = person.Role.ToString() == role;
                    // CreatedAt is a UTC instant captured during the call (R1.5). SQLite round-trips
                    // DateTime without a Kind, so assert the value falls in [before, after] rather
                    // than relying on DateTimeKind.
                    var createdAtIsUtcNow = person.CreatedAt >= before.AddSeconds(-1)
                        && person.CreatedAt <= after.AddSeconds(1);

                    return hashIsNotPlaintext
                        && hashDoesNotEmbedPlaintext
                        && hashVerifies
                        && roleMatches
                        && createdAtIsUtcNow;
                },
                iter: 100);
    }

    /// <summary>
    /// Property 3 — Duplicate-email registration is rejected and persists nothing.
    ///
    /// Given a Person already stored with an email, registering again with the SAME email
    /// returns a Conflict failure and leaves the Person set unchanged (no second row).
    ///
    /// Feature: streaming-panel, Property 3: Duplicate-email registration is rejected and persists nothing
    /// Validates: Requirements 1.3
    /// </summary>
    [Fact]
    public void DuplicateEmailRegistrationIsRejectedAndPersistsNothing()
    {
        (from email in GenEmail
         from firstPassword in GenPassword
         from secondPassword in GenPassword
         from firstRole in GenRole
         from secondRole in GenRole
         select (email, firstPassword, secondPassword, firstRole, secondRole))
            .Sample(
                input =>
                {
                    var (email, firstPassword, secondPassword, firstRole, secondRole) = input;

                    using var connection = OpenDatabase();
                    var options = BuildOptions(connection);

                    // Seed the first registration (succeeds).
                    using (var ctx = new SqliteAuthDbContext(options))
                    {
                        var service = BuildService(ctx);
                        var first = service
                            .RegisterAsync(new RegisterRequest(email, firstPassword, firstRole, null))
                            .GetAwaiter().GetResult();
                        if (!first.IsSuccess)
                        {
                            return false;
                        }
                    }

                    int countBefore;
                    using (var ctx = new SqliteAuthDbContext(options))
                    {
                        countBefore = ctx.Persons.Count();
                    }

                    // Register again with the SAME email → must be a Conflict, no new row.
                    Result<AuthResponse> second;
                    using (var ctx = new SqliteAuthDbContext(options))
                    {
                        var service = BuildService(ctx);
                        second = service
                            .RegisterAsync(new RegisterRequest(email, secondPassword, secondRole, null))
                            .GetAwaiter().GetResult();
                    }

                    using var verify = new SqliteAuthDbContext(options);
                    var countAfter = verify.Persons.Count();

                    return second.IsFailure
                        && second.ErrorCode == ErrorCode.Conflict
                        && countAfter == countBefore;
                },
                iter: 100);
    }

    /// <summary>
    /// Property 6 — Login rejects non-matching and malformed credentials without issuing tokens.
    ///
    /// For a stored Person, logging in with a wrong password, a non-existent email, or empty/garbage
    /// credentials yields a 401 Unauthorized failure and persists NO refresh token (the RefreshTokens
    /// count is unchanged from before the attempt). Service-layer rejection is asserted here; field
    /// length/absence (R2.6) is additionally enforced at the validator layer.
    ///
    /// Feature: streaming-panel, Property 6: Login rejects non-matching and malformed credentials without issuing tokens
    /// Validates: Requirements 2.5, 2.6
    /// </summary>
    [Fact]
    public void LoginRejectsNonMatchingAndMalformedCredentialsWithoutIssuingTokens()
    {
        (from email in GenEmail
         from correctPassword in GenPassword
         from role in GenRole
         // A deliberately bad login attempt: wrong password, unknown email, or malformed/empty input.
         from attempt in Gen.OneOf(
             GenPassword.Select(wrong => (useStoredEmail: true, password: wrong)),              // wrong password
             GenEmail.Select(other => (useStoredEmail: false, password: correctPassword)),      // unknown email
             Gen.OneOfConst("", " ", "x", new string('z', 300)).Select(g => (useStoredEmail: false, password: g)))
         select (email, correctPassword, role, attempt))
            .Sample(
                input =>
                {
                    var (email, correctPassword, role, attempt) = input;

                    using var connection = OpenDatabase();
                    var options = BuildOptions(connection);

                    // Seed one real Person to log in against.
                    using (var ctx = new SqliteAuthDbContext(options))
                    {
                        var service = BuildService(ctx);
                        var reg = service
                            .RegisterAsync(new RegisterRequest(email, correctPassword, role, null))
                            .GetAwaiter().GetResult();
                        if (!reg.IsSuccess)
                        {
                            return false;
                        }
                    }

                    int refreshBefore;
                    using (var ctx = new SqliteAuthDbContext(options))
                    {
                        refreshBefore = ctx.RefreshTokens.Count();
                    }

                    // Build an attempt that must NOT authenticate. When reusing the stored email we
                    // guarantee the password differs; otherwise we use a non-existent email.
                    var attemptEmail = attempt.useStoredEmail ? email : attempt.password + "@nobody.example";
                    var attemptPassword = attempt.useStoredEmail
                        ? (attempt.password == correctPassword ? correctPassword + "\u0001x" : attempt.password)
                        : attempt.password;

                    // Use an unknown email directly for the unknown-email / malformed cases.
                    if (!attempt.useStoredEmail)
                    {
                        attemptEmail = $"unused-{Guid.NewGuid():N}@nobody.example";
                    }

                    Result<AuthResponse> login;
                    using (var ctx = new SqliteAuthDbContext(options))
                    {
                        var service = BuildService(ctx);
                        login = service
                            .LoginAsync(new LoginRequest(attemptEmail, attemptPassword))
                            .GetAwaiter().GetResult();
                    }

                    using var verify = new SqliteAuthDbContext(options);
                    var refreshAfter = verify.RefreshTokens.Count();

                    return login.IsFailure
                        && login.ErrorCode == ErrorCode.Unauthorized
                        && refreshAfter == refreshBefore;
                },
                iter: 100);
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

    private static AuthService BuildService(AppDbContext ctx)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "test-signing-key-at-least-32-bytes-long-0123456789",
                ["Jwt:Issuer"] = "streaming-panel-tests",
                ["Jwt:Audience"] = "streaming-panel-tests",
                ["Jwt:RefreshGraceSeconds"] = "8",
            })
            .Build();

        var hasher = new PasswordHasher();
        var tokenGenerator = new JwtTokenGenerator(configuration);
        var refreshTokens = new RefreshTokenRepository(ctx);

        return new AuthService(ctx, hasher, tokenGenerator, refreshTokens, configuration);
    }
}

/// <summary>
/// Test-only <see cref="AppDbContext"/> that adds a JSON value converter for
/// <see cref="Movie.MainActors"/> so the PostgreSQL <c>text[]</c> mapping can be
/// stored on SQLite. All production configurations still apply via the base
/// <see cref="AppDbContext.OnModelCreating"/>; this only overrides the one mapping
/// SQLite cannot represent natively (mirrors the Persistence test harness).
/// </summary>
internal sealed class SqliteAuthDbContext : AppDbContext
{
    public SqliteAuthDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var listToJson = new ValueConverter<List<string>, string>(
            v => string.Join('\u001f', v),
            v => string.IsNullOrEmpty(v)
                ? new List<string>()
                : v.Split(new[] { '\u001f' }, StringSplitOptions.None).ToList());

        var listComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
            v => v == null ? 0 : v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode())),
            v => v.ToList());

        modelBuilder.Entity<Movie>()
            .Property(m => m.MainActors)
            .HasConversion(listToJson, listComparer);
    }
}
