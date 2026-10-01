using StreamingPanel.Core.Entities;

namespace StreamingPanel.Core.Interfaces;

/// <summary>
/// Persistence boundary for <see cref="RefreshToken"/> rows. Everything is keyed by
/// the token's hash; the raw token never reaches the database.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Looks up a token by its hash, including its owning person. Returns the row even
    /// when revoked or expired so the auth service can tell rotation, reuse, and
    /// grace-window cases apart. Null when no row matches.
    /// </summary>
    Task<RefreshToken?> GetActiveAsync(string tokenHash);

    Task AddAsync(RefreshToken token);

    /// <summary>
    /// Marks the token revoked, optionally recording the successor hash. Call
    /// <see cref="SaveChangesAsync"/> to persist.
    /// </summary>
    Task InvalidateAsync(RefreshToken token);

    Task SaveChangesAsync();
}
