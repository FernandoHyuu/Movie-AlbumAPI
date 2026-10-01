namespace StreamingPanel.Core.Entities;

/// <summary>
/// A long-lived token used to obtain a new access token without re-entering
/// credentials. Only the hash is stored, never the raw token.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }

    public string TokenHash { get; set; } = default!;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    // Hash of the token that superseded this one at rotation.
    public string? ReplacedByTokenHash { get; set; }

    public Person? Person { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;

    /// <summary>
    /// A revoked token can still re-serve its successor pair if presented within the
    /// grace window of its revocation. This absorbs concurrent rotation requests
    /// (e.g. two tabs refreshing at once) instead of treating them as token reuse.
    /// </summary>
    public bool IsWithinGraceWindow(TimeSpan graceWindow) =>
        RevokedAt is DateTime revoked && DateTime.UtcNow <= revoked + graceWindow;
}
