using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Api.Middleware;

/// <summary>
/// Wraps a domain failure (<see cref="ErrorCode"/> + message) as an exception so a failure
/// that surfaces as a throw, rather than through the normal <see cref="Result"/> return path,
/// is still mapped by <see cref="ExceptionMiddleware"/> to the right status instead of leaking
/// as a generic 500.
/// </summary>
public sealed class ResultException : Exception
{
    /// <summary>The domain failure classification carried by this exception.</summary>
    public ErrorCode ErrorCode { get; }

    /// <summary>
    /// Creates a <see cref="ResultException"/> for the given <paramref name="errorCode"/> with an
    /// optional human-readable <paramref name="message"/> safe to surface to clients.
    /// </summary>
    public ResultException(ErrorCode errorCode, string? message = null)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
