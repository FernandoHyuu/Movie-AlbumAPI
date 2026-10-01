using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Api.Middleware;

/// <summary>
/// Global exception-handling middleware producing RFC 7807 Problem Details responses for
/// failures that reach the top of the pipeline. It:
/// <list type="bullet">
/// <item>maps a FluentValidation <see cref="ValidationException"/> to a 400
/// <see cref="ValidationProblemDetails"/> keyed by field name;</item>
/// <item>maps a <see cref="ResultException"/> to the status its <see cref="ErrorCode"/> dictates;</item>
/// <item>maps any other exception to a generic 500 whose detail excludes stack traces, source
/// paths, and type names, so nothing internal leaks to the client.</item>
/// </list>
/// It writes a response only when none has started, so it never corrupts a partially written
/// body. Rollback of partial work is handled by the service layer's unit of work.
/// </summary>
public sealed class ExceptionMiddleware
{
    /// <summary>RFC 7807 media type used for every error response.</summary>
    public const string ProblemJsonContentType = "application/problem+json";

    private const int MaxTitleLength = 200;
    private const int MaxDetailLength = 1000;

    private const string GenericErrorTitle = "An unexpected error occurred.";
    private const string GenericErrorDetail =
        "The server encountered an unexpected error while processing the request. Please try again later.";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Invokes the next component and converts any escaping exception to Problem Details.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException validationException)
        {
            await WriteValidationProblemAsync(context, validationException);
        }
        catch (ResultException resultException)
        {
            await WriteResultProblemAsync(context, resultException);
        }
        catch (Exception exception)
        {
            // Unmapped: log the full detail server-side, but never surface internals to the client.
            _logger.LogError(exception, "Unhandled exception processing {Method} {Path}.",
                context.Request.Method, context.Request.Path);
            await WriteGenericProblemAsync(context);
        }
    }

    private static Task WriteValidationProblemAsync(HttpContext context, ValidationException exception)
    {
        // Group failing checks by property name so each field carries its own array of messages.
        var errors = exception.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = Truncate("One or more validation errors occurred.", MaxTitleLength),
            Detail = Truncate("The request failed validation. See the errors member for details.", MaxDetailLength)
        };

        return WriteProblemAsync(context, problem, StatusCodes.Status400BadRequest);
    }

    private static Task WriteResultProblemAsync(HttpContext context, ResultException exception)
    {
        var statusCode = ErrorCodeHttpMapping.ToStatusCode(exception.ErrorCode);

        // Use the caller-provided message when present (these are safe domain messages); otherwise
        // fall back to the generic, leak-free text so nothing internal is exposed.
        var detail = string.IsNullOrWhiteSpace(exception.Message)
            ? GenericErrorDetail
            : exception.Message;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = Truncate(TitleFor(statusCode), MaxTitleLength),
            Detail = Truncate(detail, MaxDetailLength)
        };

        return WriteProblemAsync(context, problem, statusCode);
    }

    private static Task WriteGenericProblemAsync(HttpContext context)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = Truncate(GenericErrorTitle, MaxTitleLength),
            Detail = Truncate(GenericErrorDetail, MaxDetailLength)
        };

        return WriteProblemAsync(context, problem, StatusCodes.Status500InternalServerError);
    }

    private static async Task WriteProblemAsync(HttpContext context, ProblemDetails problem, int statusCode)
    {
        // If the response has already started we cannot safely rewrite it, so stop here.
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = ProblemJsonContentType;

        var payload = JsonSerializer.Serialize(problem, problem.GetType(), SerializerOptions);
        await context.Response.WriteAsync(payload);
    }

    private static string TitleFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
        _ => GenericErrorTitle
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
