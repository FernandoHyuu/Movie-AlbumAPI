using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Core.Interfaces;

/// <summary>
/// Authentication flows: register, login, and refresh-token rotation. Expected
/// failures (duplicate email, bad credentials, invalid token, database down) come back
/// as a failed <see cref="Result{T}"/> rather than an exception.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Registers a new Person. A duplicate email yields <see cref="ErrorCode.Conflict"/>;
    /// otherwise the password is hashed, <c>CreatedAt</c> is stamped in UTC, and a token
    /// pair is issued and persisted.
    /// </summary>
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request);

    /// <summary>
    /// Authenticates by email and password. Bad or unknown credentials yield
    /// <see cref="ErrorCode.Unauthorized"/>; a database outage yields
    /// <see cref="ErrorCode.Unavailable"/>. On success a token pair is issued and persisted.
    /// </summary>
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request);

    /// <summary>
    /// Rotates the presented refresh token into a fresh pair, revoking the old token and
    /// applying the grace window to its immediate predecessor.
    /// </summary>
    Task<Result<AuthResponse>> RefreshAsync(string refreshToken);
}
