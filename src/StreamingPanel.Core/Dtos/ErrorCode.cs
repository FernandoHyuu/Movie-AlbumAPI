namespace StreamingPanel.Core.Dtos;

/// <summary>
/// Expected domain failures a service can signal through <see cref="Result"/>.
/// Controllers map each code to its HTTP status, so these flows never need to throw.
/// </summary>
public enum ErrorCode
{
    None = 0,

    /// <summary>Validation failure. Maps to 400.</summary>
    Validation,

    /// <summary>Bad or missing credentials/token. Maps to 401.</summary>
    Unauthorized,

    /// <summary>Authenticated but not permitted. Maps to 403.</summary>
    Forbidden,

    /// <summary>Resource does not exist. Maps to 404.</summary>
    NotFound,

    /// <summary>State conflict, e.g. duplicate email. Maps to 409.</summary>
    Conflict,

    /// <summary>Unexpected server-side failure. Maps to 500.</summary>
    Internal,

    /// <summary>A dependency such as the database is down. Maps to 503.</summary>
    Unavailable
}
