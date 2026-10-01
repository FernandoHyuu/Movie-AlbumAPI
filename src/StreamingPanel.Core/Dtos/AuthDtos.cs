namespace StreamingPanel.Core.Dtos;

/// <summary>
/// Request to register a new Person. <see cref="Role"/> is the string name of a
/// <see cref="Enums.Role"/>.
/// </summary>
public record RegisterRequest(string Email, string Password, string Role, string? Name);

/// <summary>Request to log in with email and password.</summary>
public record LoginRequest(string Email, string Password);

/// <summary>Request to exchange a refresh token for a new token pair.</summary>
public record RefreshRequest(string RefreshToken);

/// <summary>
/// The issued token pair plus the authenticated role and the access-token expiry (UTC).
/// </summary>
public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    string Role,
    DateTime AccessTokenExpiresAt);
