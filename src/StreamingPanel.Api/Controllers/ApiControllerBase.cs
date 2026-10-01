using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using StreamingPanel.Api.Middleware;
using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Api.Controllers;

/// <summary>
/// Shared base for the thin API controllers. Translates a <see cref="Result"/> /
/// <see cref="Result{T}"/> outcome into an <see cref="IActionResult"/>: success maps to the
/// appropriate 2xx status (200/201/204) and failure maps to an RFC 7807
/// <see cref="ProblemDetails"/> response whose status comes from the <see cref="ErrorCode"/>,
/// keeping the controllers free of HTTP status-mapping logic.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Validates <paramref name="instance"/> and throws a <see cref="ValidationException"/> on
    /// failure, which <see cref="ExceptionMiddleware"/> turns into a 400 listing every bad field.
    /// </summary>
    protected static async Task ValidateAsync<T>(IValidator<T> validator, T instance)
    {
        var result = await validator.ValidateAsync(instance);
        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }

    /// <summary>
    /// Maps a valueless <see cref="Result"/>: <paramref name="onSuccess"/> on success
    /// (defaulting to 204 No Content), or a mapped problem response on failure.
    /// </summary>
    protected IActionResult ToActionResult(Result result, IActionResult? onSuccess = null)
    {
        if (result.IsSuccess)
        {
            return onSuccess ?? NoContent();
        }

        return Problem(result.ErrorCode, result.ErrorMessage);
    }

    /// <summary>Maps a <see cref="Result{T}"/> to a 200 OK with the value, or a problem response.</summary>
    protected IActionResult ToActionResult<T>(Result<T> result)
    {
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.ErrorCode, result.ErrorMessage);
    }

    /// <summary>Maps a successful <see cref="Result{T}"/> to a 201 Created, or a problem response.</summary>
    protected IActionResult ToCreatedResult<T>(Result<T> result, string actionName, Func<T, object> routeValues)
    {
        return result.IsSuccess
            ? CreatedAtAction(actionName, routeValues(result.Value), result.Value)
            : Problem(result.ErrorCode, result.ErrorMessage);
    }

    /// <summary>
    /// Builds an RFC 7807 problem response for a domain failure, with the status mapped from
    /// <paramref name="errorCode"/>.
    /// </summary>
    private IActionResult Problem(ErrorCode errorCode, string? message)
    {
        var statusCode = ErrorCodeHttpMapping.ToStatusCode(errorCode);
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = TitleFor(statusCode),
            Detail = string.IsNullOrWhiteSpace(message) ? null : message
        };

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { ExceptionMiddleware.ProblemJsonContentType }
        };
    }

    private static string TitleFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
        _ => "An unexpected error occurred."
    };
}
