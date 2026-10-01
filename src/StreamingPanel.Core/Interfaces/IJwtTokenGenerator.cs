using StreamingPanel.Core.Entities;

namespace StreamingPanel.Core.Interfaces;

/// <summary>
/// Result of creating a refresh token. The raw token is handed back to the client but
/// never persisted; the entity stores only its hash.
/// </summary>
public readonly record struct RefreshTokenResult(string RawToken, RefreshToken Entity);

/// <summary>
/// Issues the two tokens of an authenticated session: a short-lived signed JWT access
/// token and a long-lived opaque refresh token.
/// </summary>
public interface IJwtTokenGenerator
{
    /// <summary>
    /// Creates a 15-minute signed JWT embedding the sub (person id), email, and role
    /// claims. Signing key, issuer, and audience come from configuration.
    /// </summary>
    string CreateAccessToken(Person person);

    /// <summary>
    /// Creates a 256-bit opaque refresh token with a 7-day expiry. The returned entity
    /// stores only the hash; the raw token is exposed on the result for return to the client.
    /// </summary>
    RefreshTokenResult CreateRefreshToken(Guid personId);
}
