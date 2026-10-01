using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
/// Property-based tests for <see cref="AuthService.RefreshAsync"/> rotation and rejection.
///
/// Feature: streaming-panel, Property 7: Refresh token rotation invalidates the presented token, except the immediate predecessor within the grace window
/// Feature: streaming-panel, Property 8: Invalid or stale refresh tokens are rejected without issuance
///
/// Validates: Requirements 3.2, 3.3, 3.4, 3.5
///
/// Database provider: Microsoft.EntityFrameworkCore.Sqlite (in-memory), mirroring
/// <see cref="AuthServiceRegistrationLoginPropertyTests"/>. The real <see cref="AppDbContext"/>,
/// <see cref="PasswordHasher"/>, <see cref="JwtTokenGenerator"/>, and
/// <see cref="RefreshTokenRepository"/> run unchanged against a relational store, so the
/// production rotation + grace-window logic is exercised end-to-end. Each sample uses a
/// fresh in-memory database; every property runs a minimum of 100 iterations.
///
/// Static grace cache: <see cref="AuthService"/> keeps a process-wide
/// <c>ConcurrentDictionary</c> of just-issued successor pairs keyed by the SHA-256 hash of
/// the <em>predecessor</em> raw token. Because every refresh token carries 256 bits of
/// entropy, the predecessor hash is effectively unique per rotation, so distinct scenarios
/// cannot collide across tests or iterations. The grace=0 rejection path relies on this:
/// a token rotated under a grace=0 service is cached with an already-elapsed expiry, so the
/// predecessor re-presentation misses the cache and is rejected — no reliance on wall-clock
/// sleeping.
/// </summary>
public class AuthServiceRefreshRotationPropertyTests
{
    private const int GraceSeconds = 8;

    private static readonly string[] ValidRoles =
    {
        nameof(Role.Admin),
        nameof(Role.User_Movie),
        nameof(Role.User_Album),
        nameof(Role.User_Full),
    };

    private static readonly Gen<string> GenEmail =
        from local in Gen.Char['a', 'z'].Array[1, 20]
        from domain in Gen.Char['a', 'z'].Array[1, 15]
        from tld in Gen.OneOfConst("com", "org", "net", "io", "dev")
        select $"{new string(local)}@{new string(domain)}.{tld}";

    private static readonly Gen<string> GenPassword =
        Gen.Char[' ', '~'].Array[8, 40].Select(cs => new string(cs));

    private static readonly Gen<string> GenRole = Gen.OneOfConst(ValidRoles);

    /// <summary>
    /// Property 7 — Rotation invalidates the presented token, except the immediate predecessor
    /// within the grace window.
    ///
    /// Starting from a freshly issued refresh token, rotating it (1) succeeds with a brand-new
    /// pair whose refresh token differs from the one presented, and (2) revokes the presented
    /// token linking it to its successor. Re-presenting that same predecessor immediately,
    /// still inside the grace window, re-serves the IDENTICAL successor pair (no re-rotation,
    /// no new chain link, no additional refresh row). The successor remains independently
    /// usable: rotating it yields yet another distinct pair.
    ///
    /// Feature: streaming-panel, Property 7: Refresh token rotation invalidates the presented token, except the immediate predecessor within the grace window
    /// Validates: Requirements 3.4, 3.5
    /// </summary>
    [Fact]
    public void RotationInvalidatesPresentedTokenExceptPredecessorWithinGraceWindow()
    {
        (from email in GenEmail
         from password in GenPassword
         from role in GenRole
         select (email, password, role))
            .Sample(
                input =>
                {
                    var (email, password, role) = input;

                    using var connection = OpenDatabase();
                    var options = BuildOptions(connection);

                    // Register to obtain the initial refresh token.
                    AuthResponse initial;
                    using (var ctx = new SqliteRefreshDbContext(options))
                    {
                        var service = BuildService(ctx, GraceSeconds);
                        var reg = service
                            .RegisterAsync(new RegisterRequest(email, password, role, null))
                            .GetAwaiter().GetResult();
                        if (!reg.IsSuccess)
                        {
                            return false;
                        }

                        initial = reg.Value;
                    }

                    // Rotate the initial token → a fresh, distinct pair.
                    Result<AuthResponse> rotated;
                    using (var ctx = new SqliteRefreshDbContext(options))
                    {
                        var service = BuildService(ctx, GraceSeconds);
                        rotated = service.RefreshAsync(initial.RefreshToken).GetAwaiter().GetResult();
                    }

                    if (rotated.IsFailure
                        || rotated.Value.RefreshToken == initial.RefreshToken)
                    {
                        return false;
                    }

                    var successorRaw = rotated.Value.RefreshToken;

                    // The presented (initial) token must now be revoked and linked to a successor.
                    int rowsAfterRotation;
                    using (var verify = new SqliteRefreshDbContext(options))
                    {
                        var presentedHash = JwtTokenGenerator.HashToken(initial.RefreshToken);
                        var presented = verify.RefreshTokens.Single(rt => rt.TokenHash == presentedHash);
                        if (presented.RevokedAt is null
                            || presented.ReplacedByTokenHash is null
                            || presented.IsActive)
                        {
                            return false;
                        }

                        rowsAfterRotation = verify.RefreshTokens.Count();
                    }

                    // Re-present the predecessor WITHIN the grace window → identical successor pair,
                    // no re-rotation and no additional refresh-token row.
                    Result<AuthResponse> graceReplay;
                    using (var ctx = new SqliteRefreshDbContext(options))
                    {
                        var service = BuildService(ctx, GraceSeconds);
                        graceReplay = service.RefreshAsync(initial.RefreshToken).GetAwaiter().GetResult();
                    }

                    int rowsAfterReplay;
                    using (var verify = new SqliteRefreshDbContext(options))
                    {
                        rowsAfterReplay = verify.RefreshTokens.Count();
                    }

                    if (graceReplay.IsFailure
                        || graceReplay.Value.RefreshToken != successorRaw
                        || graceReplay.Value.AccessToken != rotated.Value.AccessToken
                        || rowsAfterReplay != rowsAfterRotation)
                    {
                        return false;
                    }

                    // The successor itself is a valid, active token: rotating it yields a new,
                    // distinct pair (the chain advances).
                    Result<AuthResponse> successorRotated;
                    using (var ctx = new SqliteRefreshDbContext(options))
                    {
                        var service = BuildService(ctx, GraceSeconds);
                        successorRotated = service.RefreshAsync(successorRaw).GetAwaiter().GetResult();
                    }

                    return successorRotated.IsSuccess
                        && successorRotated.Value.RefreshToken != successorRaw
                        && successorRotated.Value.RefreshToken != initial.RefreshToken;
                },
                iter: 100);
    }

    /// <summary>
    /// Property 8 — Invalid or stale refresh tokens are rejected without issuance.
    ///
    /// Across four rejection shapes — an unknown (never-issued) raw token, an expired token
    /// row, a revoked token presented past the grace window, and an older ancestor token in
    /// a rotated chain — <see cref="AuthService.RefreshAsync"/> returns 401 Unauthorized and
    /// persists no new refresh-token row (the RefreshTokens count is unchanged by the attempt).
    ///
    /// The "past the grace window" case uses a service configured with
    /// <c>Jwt:RefreshGraceSeconds = 0</c>: a token rotated under that service is cached with an
    /// already-elapsed expiry, so re-presenting the predecessor misses the grace cache and is
    /// rejected — no wall-clock sleeping required. Because the grace cache is keyed by the
    /// 256-bit predecessor hash, these fresh tokens never collide with other scenarios.
    ///
    /// Feature: streaming-panel, Property 8: Invalid or stale refresh tokens are rejected without issuance
    /// Validates: Requirements 3.2, 3.3, 3.5
    /// </summary>
    [Fact]
    public void InvalidOrStaleRefreshTokensAreRejectedWithoutIssuance()
    {
        (from email in GenEmail
         from password in GenPassword
         from role in GenRole
         from shape in Gen.OneOfConst(
             RejectionShape.Unknown,
             RejectionShape.Expired,
             RejectionShape.RevokedPastGrace,
             RejectionShape.OlderAncestor)
         // A random never-issued raw token for the Unknown case.
         from unknownRaw in Gen.Char[' ', '~'].Array[16, 64].Select(cs => new string(cs))
         select (email, password, role, shape, unknownRaw))
            .Sample(
                input =>
                {
                    var (email, password, role, shape, unknownRaw) = input;

                    using var connection = OpenDatabase();
                    var options = BuildOptions(connection);

                    // Register a real owner so expired/ancestor rows have a valid PersonId.
                    Guid personId;
                    AuthResponse initial;
                    using (var ctx = new SqliteRefreshDbContext(options))
                    {
                        var service = BuildService(ctx, GraceSeconds);
                        var reg = service
                            .RegisterAsync(new RegisterRequest(email, password, role, null))
                            .GetAwaiter().GetResult();
                        if (!reg.IsSuccess)
                        {
                            return false;
                        }

                        initial = reg.Value;
                        personId = ctx.Persons.Single(p => p.Email == email).Id;
                    }

                    // The raw token we will present, and the service used to present it.
                    string presentedRaw;
                    int graceForPresentation = GraceSeconds;

                    switch (shape)
                    {
                        case RejectionShape.Unknown:
                            // A random string that was never issued → no matching hash row.
                            presentedRaw = unknownRaw;
                            break;

                        case RejectionShape.Expired:
                        {
                            // Insert a refresh-token row whose ExpiresAt is in the past, with a
                            // known hash, then present the matching raw token.
                            presentedRaw = $"expired-{Guid.NewGuid():N}";
                            using var ctx = new SqliteRefreshDbContext(options);
                            ctx.RefreshTokens.Add(new RefreshToken
                            {
                                Id = Guid.NewGuid(),
                                PersonId = personId,
                                TokenHash = JwtTokenGenerator.HashToken(presentedRaw),
                                CreatedAt = DateTime.UtcNow.AddDays(-10),
                                ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
                            });
                            ctx.SaveChanges();
                            break;
                        }

                        case RejectionShape.RevokedPastGrace:
                        {
                            // Rotate the initial token under a grace=0 service: the predecessor is
                            // cached with an already-elapsed expiry, so re-presenting it misses the
                            // grace cache → 401. Present the predecessor against the same grace=0
                            // service (a fresh instance; the static cache is shared across instances).
                            using (var ctx = new SqliteRefreshDbContext(options))
                            {
                                var service = BuildService(ctx, graceSeconds: 0);
                                var rotate = service.RefreshAsync(initial.RefreshToken).GetAwaiter().GetResult();
                                if (rotate.IsFailure)
                                {
                                    return false;
                                }
                            }

                            presentedRaw = initial.RefreshToken; // the now-revoked predecessor
                            graceForPresentation = 0;
                            break;
                        }

                        case RejectionShape.OlderAncestor:
                        {
                            // Rotate twice so the ORIGINAL token is an older ancestor (two links back).
                            // The chain is rotated under a grace=0 service so the original's cached
                            // successor pair expires immediately: re-presenting the stale ancestor
                            // then misses the grace cache and is rejected (R3.5 reuse). Rotating under
                            // grace=8 instead would re-serve the still-cached successor within the
                            // window — correct grace behavior, but not the stale-reuse case under test.
                            AuthResponse gen1;
                            using (var ctx = new SqliteRefreshDbContext(options))
                            {
                                var service = BuildService(ctx, graceSeconds: 0);
                                var r1 = service.RefreshAsync(initial.RefreshToken).GetAwaiter().GetResult();
                                if (r1.IsFailure)
                                {
                                    return false;
                                }

                                gen1 = r1.Value;
                            }

                            using (var ctx = new SqliteRefreshDbContext(options))
                            {
                                var service = BuildService(ctx, graceSeconds: 0);
                                var r2 = service.RefreshAsync(gen1.RefreshToken).GetAwaiter().GetResult();
                                if (r2.IsFailure)
                                {
                                    return false;
                                }
                            }

                            presentedRaw = initial.RefreshToken; // stale ancestor, two links back
                            graceForPresentation = 0;
                            break;
                        }

                        default:
                            return false;
                    }

                    // Count refresh rows immediately before the rejected attempt.
                    int rowsBefore;
                    using (var verify = new SqliteRefreshDbContext(options))
                    {
                        rowsBefore = verify.RefreshTokens.Count();
                    }

                    Result<AuthResponse> attempt;
                    using (var ctx = new SqliteRefreshDbContext(options))
                    {
                        var service = BuildService(ctx, graceForPresentation);
                        attempt = service.RefreshAsync(presentedRaw).GetAwaiter().GetResult();
                    }

                    int rowsAfter;
                    using (var verify = new SqliteRefreshDbContext(options))
                    {
                        rowsAfter = verify.RefreshTokens.Count();
                    }

                    return attempt.IsFailure
                        && attempt.ErrorCode == ErrorCode.Unauthorized
                        && rowsAfter == rowsBefore;
                },
                iter: 100);
    }

    private enum RejectionShape
    {
        Unknown,
        Expired,
        RevokedPastGrace,
        OlderAncestor,
    }

    // ---- Shared test harness -------------------------------------------------

    private static SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = BuildOptions(connection);
        using var ctx = new SqliteRefreshDbContext(options);
        ctx.Database.EnsureCreated();

        return connection;
    }

    private static DbContextOptions<AppDbContext> BuildOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

    private static AuthService BuildService(AppDbContext ctx, int graceSeconds)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "test-signing-key-at-least-32-bytes-long-0123456789",
                ["Jwt:Issuer"] = "streaming-panel-tests",
                ["Jwt:Audience"] = "streaming-panel-tests",
                ["Jwt:RefreshGraceSeconds"] = graceSeconds.ToString(),
            })
            .Build();

        var hasher = new PasswordHasher();
        var tokenGenerator = new JwtTokenGenerator(configuration);
        var refreshTokens = new RefreshTokenRepository(ctx);

        return new AuthService(ctx, hasher, tokenGenerator, refreshTokens, configuration);
    }
}

/// <summary>
/// Test-only <see cref="AppDbContext"/> adding a JSON value converter for
/// <see cref="Movie.MainActors"/> so the PostgreSQL <c>text[]</c> mapping stores on SQLite.
/// Mirrors the harness in <see cref="AuthServiceRegistrationLoginPropertyTests"/>; declared
/// separately to avoid a cross-file dependency, affecting only the one mapping SQLite cannot
/// represent natively (none of the Person/RefreshToken mappings exercised here are touched).
/// </summary>
internal sealed class SqliteRefreshDbContext : AppDbContext
{
    public SqliteRefreshDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var listToJson = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<string>, string>(
            v => string.Join('\u001f', v),
            v => string.IsNullOrEmpty(v)
                ? new List<string>()
                : v.Split(new[] { '\u001f' }, StringSplitOptions.None).ToList());

        var listComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
            v => v == null ? 0 : v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode())),
            v => v.ToList());

        modelBuilder.Entity<Movie>()
            .Property(m => m.MainActors)
            .HasConversion(listToJson, listComparer);
    }
}
