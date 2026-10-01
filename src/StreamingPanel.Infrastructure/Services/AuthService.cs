using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Enums;
using StreamingPanel.Core.Interfaces;
using StreamingPanel.Infrastructure.Persistence;
using StreamingPanel.Infrastructure.Security;

namespace StreamingPanel.Infrastructure.Services;

/// <summary>
/// Registration, login, and refresh-token rotation. Expected failures (duplicate email,
/// bad credentials, DB outage) come back as <see cref="Result{T}"/> instead of exceptions.
/// </summary>
/// <remarks>
/// Refresh rotation is the subtle part: presenting an active token swaps it for a fresh
/// pair and revokes the old one, linking it to its successor. If the same client retries
/// with the predecessor it just rotated (double-submit, flaky network) within the grace
/// window, it gets the already-issued successor pair back instead of a 401. Everything
/// else — unknown, expired, or reused-past-grace tokens — is a 401 with no new tokens.
/// </remarks>
public sealed class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly TimeSpan _refreshGraceWindow;

    /// <summary>Grace window used when <c>Jwt:RefreshGraceSeconds</c> is missing or unparseable.</summary>
    private const int DefaultRefreshGraceSeconds = 8;

    // Process-wide cache of just-issued successor pairs, keyed by the hash of the
    // predecessor that was rotated. We can't re-derive the raw refresh token from the DB
    // (only its hash is stored), so to answer a grace-window retry with the identical pair
    // we have to remember it here. Deliberately a static ConcurrentDictionary rather than
    // IMemoryCache: no DI dependency, and entries are small and pruned lazily. Downside is
    // it's per-process, so a grace retry that lands on a different instance misses.
    private static readonly ConcurrentDictionary<string, GracePairEntry> GracePairs = new();

    /// <summary>A cached successor pair and the instant it stops being re-servable.</summary>
    private readonly record struct GracePairEntry(AuthResponse Pair, DateTime ExpiresAt);

    public AuthService(
        AppDbContext db,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator tokenGenerator,
        IRefreshTokenRepository refreshTokens,
        IConfiguration configuration)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _tokenGenerator = tokenGenerator ?? throw new ArgumentNullException(nameof(tokenGenerator));
        _refreshTokens = refreshTokens ?? throw new ArgumentNullException(nameof(refreshTokens));
        ArgumentNullException.ThrowIfNull(configuration);

        // Parse defensively so a missing or malformed value falls back to the default
        // instead of throwing while the service is being constructed.
        var graceSeconds = int.TryParse(configuration["Jwt:RefreshGraceSeconds"], out var parsed)
            ? parsed
            : DefaultRefreshGraceSeconds;
        _refreshGraceWindow = TimeSpan.FromSeconds(graceSeconds);
    }

    /// <inheritdoc />
    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Only accept an exact named role. Enum.TryParse also accepts integer-formatted
        // strings ("0" parses to Role.Admin), which isn't something a client should be able
        // to send, so the GetNames check rejects those even though validation runs earlier.
        if (!Enum.TryParse<Role>(request.Role, ignoreCase: false, out var role)
            || !Enum.IsDefined(role)
            || !Enum.GetNames<Role>().Contains(request.Role, StringComparer.Ordinal))
        {
            return Result.Failure<AuthResponse>(
                ErrorCode.Validation, $"'{request.Role}' is not a recognized role.");
        }

        try
        {
            // Reject a duplicate email before persisting anything.
            var emailTaken = await _db.Persons
                .AnyAsync(p => p.Email == request.Email);
            if (emailTaken)
            {
                return Result.Failure<AuthResponse>(
                    ErrorCode.Conflict, "A person with this email already exists.");
            }

            var person = new Person
            {
                Id = Guid.NewGuid(),
                Name = request.Name ?? string.Empty,
                Email = request.Email,
                PasswordHash = _passwordHasher.Hash(request.Password), // only the hash is stored
                Role = role,
                CreatedAt = DateTime.UtcNow,
            };

            await _db.Persons.AddAsync(person);

            var response = await IssueTokenPairAsync(person);

            return Result.Success(response);
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            return Result.Failure<AuthResponse>(
                ErrorCode.Unavailable, "The database is currently unavailable.");
        }
    }

    /// <inheritdoc />
    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var person = await _db.Persons
                .SingleOrDefaultAsync(p => p.Email == request.Email);

            // Keep unknown-email and wrong-password indistinguishable so the response
            // doesn't reveal whether an email is registered.
            if (person is null
                || !_passwordHasher.Verify(request.Password, person.PasswordHash))
            {
                return Result.Failure<AuthResponse>(
                    ErrorCode.Unauthorized, "Invalid email or password.");
            }

            var response = await IssueTokenPairAsync(person);

            return Result.Success(response);
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            return Result.Failure<AuthResponse>(
                ErrorCode.Unavailable, "The database is currently unavailable.");
        }
    }

    /// <inheritdoc />
    public async Task<Result<AuthResponse>> RefreshAsync(string refreshToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);

        try
        {
            // The DB only has the hash, so hash the presented token the same way before
            // looking it up. The repository returns the row whatever its state, so we can
            // tell rotation, reuse, and the grace-window retry apart here.
            var presentedHash = JwtTokenGenerator.HashToken(refreshToken);
            var token = await _refreshTokens.GetActiveAsync(presentedHash);

            if (token is null)
            {
                return Unauthorized("The refresh token is invalid.");
            }

            // Active and unexpired: rotate it for a fresh pair.
            if (token.IsActive)
            {
                return await RotateAsync(token);
            }

            // Revoked, but it's the token we just rotated and we're still inside the grace
            // window: re-serve the successor pair instead of punishing a double-submit.
            if (token.RevokedAt is not null
                && token.ReplacedByTokenHash is not null
                && token.IsWithinGraceWindow(_refreshGraceWindow))
            {
                if (TryGetCachedSuccessor(presentedHash, out var cachedPair))
                {
                    return Result.Success(cachedPair);
                }

                // Cache miss (evicted, or a different process handled the rotation). We
                // can't reconstruct the raw token from the DB, so fall back to 401 rather
                // than hand out a divergent pair.
                return Unauthorized("The refresh token is invalid.");
            }

            // Expired, reused past the grace window, or an older link in the chain: reject.
            return Unauthorized("The refresh token is invalid.");
        }
        catch (Exception ex) when (IsDatabaseUnavailable(ex))
        {
            return Result.Failure<AuthResponse>(
                ErrorCode.Unavailable, "The database is currently unavailable.");
        }
    }

    /// <summary>
    /// Issues a fresh pair, revokes the presented token and links it to its successor, and
    /// saves both in one call. Caches the successor so a grace-window retry gets it back.
    /// </summary>
    private async Task<Result<AuthResponse>> RotateAsync(RefreshToken presented)
    {
        // The owning Person is needed to mint a role-bearing access token. The repository
        // eager-loads it, but guard in case a row came back without it.
        var person = presented.Person
            ?? await _db.Persons.SingleOrDefaultAsync(p => p.Id == presented.PersonId);
        if (person is null)
        {
            return Unauthorized("The refresh token is invalid.");
        }

        var accessToken = _tokenGenerator.CreateAccessToken(person);
        var refreshResult = _tokenGenerator.CreateRefreshToken(person.Id);

        // Revoke the presented token and point it at its successor's hash.
        presented.RevokedAt = DateTime.UtcNow;
        presented.ReplacedByTokenHash = refreshResult.Entity.TokenHash;

        await _refreshTokens.InvalidateAsync(presented);
        await _refreshTokens.AddAsync(refreshResult.Entity);
        await _refreshTokens.SaveChangesAsync();

        var response = new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: refreshResult.RawToken, // raw to the client, hash in the DB
            Role: person.Role.ToString(),
            AccessTokenExpiresAt: DateTime.UtcNow.Add(JwtTokenGenerator.AccessTokenLifetime));

        // Keyed by the predecessor's hash so a grace-window retry resolves to this pair.
        CacheSuccessor(presented.TokenHash, response);

        return Result.Success(response);
    }

    /// <summary>Caches the successor pair for the grace window, keyed by predecessor hash.</summary>
    private void CacheSuccessor(string predecessorHash, AuthResponse pair)
    {
        PruneExpiredGracePairs();
        GracePairs[predecessorHash] = new GracePairEntry(pair, DateTime.UtcNow + _refreshGraceWindow);
    }

    /// <summary>Returns the cached successor pair if present and unexpired; evicts it once stale.</summary>
    private static bool TryGetCachedSuccessor(string predecessorHash, out AuthResponse pair)
    {
        if (GracePairs.TryGetValue(predecessorHash, out var entry))
        {
            if (DateTime.UtcNow <= entry.ExpiresAt)
            {
                pair = entry.Pair;
                return true;
            }

            GracePairs.TryRemove(predecessorHash, out _);
        }

        pair = default!;
        return false;
    }

    /// <summary>Drops expired entries so the static cache doesn't grow unbounded.</summary>
    private static void PruneExpiredGracePairs()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in GracePairs)
        {
            if (now > kvp.Value.ExpiresAt)
            {
                GracePairs.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>Builds the standard 401 refresh failure.</summary>
    private static Result<AuthResponse> Unauthorized(string message) =>
        Result.Failure<AuthResponse>(ErrorCode.Unauthorized, message);

    /// <summary>
    /// Issues an access + refresh pair, persisting the refresh token (hash only) together
    /// with any pending inserts in one save. The response carries the raw refresh token.
    /// </summary>
    private async Task<AuthResponse> IssueTokenPairAsync(Person person)
    {
        var accessToken = _tokenGenerator.CreateAccessToken(person);
        var refreshResult = _tokenGenerator.CreateRefreshToken(person.Id);

        await _refreshTokens.AddAsync(refreshResult.Entity);

        // One save covers a pending new Person (register) and the refresh token together.
        await _refreshTokens.SaveChangesAsync();

        return new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: refreshResult.RawToken, // raw to the client, hash in the DB
            Role: person.Role.ToString(),
            AccessTokenExpiresAt: DateTime.UtcNow.Add(JwtTokenGenerator.AccessTokenLifetime));
    }

    /// <summary>True for connection/timeout/transient DB failures the caller maps to 503.</summary>
    private static bool IsDatabaseUnavailable(Exception ex) =>
        ex is NpgsqlException
        || ex is DbUpdateException { InnerException: NpgsqlException }
        || ex.InnerException is NpgsqlException
        || ex is TimeoutException;
}
