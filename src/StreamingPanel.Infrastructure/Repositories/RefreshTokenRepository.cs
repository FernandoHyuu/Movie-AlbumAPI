using Microsoft.EntityFrameworkCore;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Interfaces;
using StreamingPanel.Infrastructure.Persistence;

namespace StreamingPanel.Infrastructure.Repositories;

/// <summary>
/// EF Core <see cref="IRefreshTokenRepository"/> over <see cref="AppDbContext"/>. Tokens
/// are keyed by hash; the raw token never reaches persistence. Mutations aren't flushed
/// until <see cref="SaveChangesAsync"/>, so a rotation (invalidate + add) commits atomically.
/// </summary>
public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _db;

    public RefreshTokenRepository(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    public async Task<RefreshToken?> GetActiveAsync(string tokenHash)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        // Eager-load the owning Person so the auth service can mint the next access token
        // without a second round-trip. The row comes back whatever its revoked/expired
        // state — rotation and grace-window decisions belong to the caller.
        return await _db.RefreshTokens
            .Include(rt => rt.Person)
            .SingleOrDefaultAsync(rt => rt.TokenHash == tokenHash);
    }

    /// <inheritdoc />
    public async Task AddAsync(RefreshToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        await _db.RefreshTokens.AddAsync(token);
    }

    /// <inheritdoc />
    public Task InvalidateAsync(RefreshToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.RevokedAt ??= DateTime.UtcNow;

        // Attach and flag the changed columns in case the entity isn't already tracked.
        var entry = _db.Entry(token);
        if (entry.State == EntityState.Detached)
        {
            _db.RefreshTokens.Attach(token);
        }

        entry.Property(rt => rt.RevokedAt).IsModified = true;
        entry.Property(rt => rt.ReplacedByTokenHash).IsModified = true;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
