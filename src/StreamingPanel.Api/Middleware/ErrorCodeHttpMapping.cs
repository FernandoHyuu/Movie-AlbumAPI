using Microsoft.AspNetCore.Http;
using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Api.Middleware;

/// <summary>
/// Single place that maps a domain <see cref="ErrorCode"/> to its HTTP status code:
/// <list type="bullet">
/// <item><see cref="ErrorCode.Validation"/> → 400</item>
/// <item><see cref="ErrorCode.Unauthorized"/> → 401</item>
/// <item><see cref="ErrorCode.Forbidden"/> → 403</item>
/// <item><see cref="ErrorCode.NotFound"/> → 404</item>
/// <item><see cref="ErrorCode.Conflict"/> → 409</item>
/// <item><see cref="ErrorCode.Unavailable"/> → 503</item>
/// <item><see cref="ErrorCode.Internal"/> (and anything unmapped) → 500</item>
/// </list>
/// Shared by <see cref="ExceptionMiddleware"/> and the controllers so the translation lives
/// in one place.
/// </summary>
public static class ErrorCodeHttpMapping
{
    /// <summary>Returns the HTTP status code that the given <paramref name="errorCode"/> maps to.</summary>
    public static int ToStatusCode(ErrorCode errorCode) => errorCode switch
    {
        ErrorCode.Validation => StatusCodes.Status400BadRequest,
        ErrorCode.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCode.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCode.NotFound => StatusCodes.Status404NotFound,
        ErrorCode.Conflict => StatusCodes.Status409Conflict,
        ErrorCode.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };
}
