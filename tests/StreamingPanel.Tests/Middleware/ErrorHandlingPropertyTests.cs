using System.Text;
using System.Text.Json;
using CsCheck;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using StreamingPanel.Api.Middleware;
using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Tests.Middleware;

/// <summary>
/// Property-based tests for <see cref="ExceptionMiddleware"/>, the RFC 7807 global
/// exception handler (R9.1–R9.5, R10.5, R12.5).
///
/// Each property drives the middleware directly: it builds a <see cref="DefaultHttpContext"/>
/// whose response body is a <see cref="MemoryStream"/>, wires a <see cref="RequestDelegate"/>
/// that throws a generated exception, invokes <see cref="ExceptionMiddleware.InvokeAsync"/>,
/// then deserialises the written <c>application/problem+json</c> payload with
/// <see cref="System.Text.Json"/> to assert on its shape. No database or live host is involved;
/// the middleware itself performs no persistence, so the "persists nothing" clauses are asserted
/// as the structural contract that the error path writes only an error Problem Details body and
/// never a success payload (transactional rollback is enforced by the service unit of work,
/// exercised in task 7.7).
///
/// Each property runs a minimum of 100 iterations.
///
/// Feature: streaming-panel
/// Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 10.5, 12.5
/// </summary>
public class ErrorHandlingPropertyTests
{
    private const int Iterations = 100;

    private const int MaxTitleLength = 200;
    private const int MaxDetailLength = 1000;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Runs the generated <paramref name="thrower"/> through <see cref="ExceptionMiddleware"/> and
    /// returns the HTTP status, content type, and the parsed JSON body document that was written.
    /// </summary>
    private static async Task<(int Status, string? ContentType, JsonDocument Body)> RunAsync(
        RequestDelegate thrower)
    {
        var context = new DefaultHttpContext();
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        var middleware = new ExceptionMiddleware(thrower, NullLogger<ExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);

        buffer.Position = 0;
        var json = Encoding.UTF8.GetString(buffer.ToArray());
        var document = JsonDocument.Parse(string.IsNullOrEmpty(json) ? "{}" : json);

        return (context.Response.StatusCode, context.Response.ContentType, document);
    }

    private static RequestDelegate Throwing(Exception exception) => _ => throw exception;

    /// <summary>True when <paramref name="haystack"/> contains <paramref name="needle"/> ignoring case.</summary>
    private static bool ContainsCi(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    // =====================================================================
    // Property 20: Error responses are well-formed and leak-free
    // Validates: Requirements 9.1, 9.2, 9.3
    // =====================================================================

    /// <summary>
    /// Feature: streaming-panel, Property 20: Error responses are well-formed and leak-free
    ///
    /// For any arbitrary unmapped exception — including messages that embed fake absolute paths,
    /// stack-frame markers, and type-name-like text — the middleware writes an
    /// <c>application/problem+json</c> response (R9.1) carrying a numeric status, a non-empty
    /// title ≤200 chars, and a non-empty detail ≤1000 chars (R9.2). The status is 500 and neither
    /// the title nor the detail reproduces the exception's type name, raw message, source paths, or
    /// stack-trace markers (R9.3).
    ///
    /// Validates: Requirements 9.1, 9.2, 9.3
    /// </summary>
    [Fact]
    public void Property20_ErrorResponses_AreWellFormedAndLeakFree()
    {
        // A message deliberately salted with content that MUST NOT escape to the client:
        // a Windows source path, a ".cs:line" marker, a stack-frame " at " marker, and a
        // recognisable type name.
        var gen =
            from leadingText in Gen.String[Gen.Char.AlphaNumeric, 0, 40]
            from secret in Gen.OneOfConst(
                @"C:\PROGRAMA\src\StreamingPanel.Api\Secret.cs:line 42",
                "   at StreamingPanel.Core.Services.AuthService.Login()",
                "NullReferenceException: object reference not set",
                @"/var/www/app/Internal/Repository.cs")
            from kind in Gen.Int[0, 3]
            select (message: $"{leadingText} {secret}".Trim(), secret, kind);

        gen.Sample(t =>
        {
            // A variety of unmapped exception types — none is handled specially by the middleware.
            Exception ex = t.kind switch
            {
                0 => new InvalidOperationException(t.message),
                1 => new NullReferenceException(t.message),
                2 => new ArgumentOutOfRangeException("param", t.message),
                _ => new Exception(t.message),
            };

            var (status, contentType, body) = RunAsync(Throwing(ex)).GetAwaiter().GetResult();
            using var bodyDoc = body;
            var root = bodyDoc.RootElement;

            // R9.1 media type.
            Assert.Equal(ExceptionMiddleware.ProblemJsonContentType, contentType);

            // R9.3 unmapped => 500.
            Assert.Equal(StatusCodes.Status500InternalServerError, status);

            // R9.2 numeric status present and matching.
            Assert.True(root.TryGetProperty("status", out var statusEl));
            Assert.Equal(JsonValueKind.Number, statusEl.ValueKind);
            Assert.Equal(status, statusEl.GetInt32());

            // R9.2 non-empty title ≤ 200.
            Assert.True(root.TryGetProperty("title", out var titleEl));
            var title = titleEl.GetString();
            Assert.False(string.IsNullOrWhiteSpace(title));
            Assert.True(title!.Length <= MaxTitleLength);

            // R9.2 non-empty detail ≤ 1000.
            Assert.True(root.TryGetProperty("detail", out var detailEl));
            var detail = detailEl.GetString();
            Assert.False(string.IsNullOrWhiteSpace(detail));
            Assert.True(detail!.Length <= MaxDetailLength);

            // R9.2 / R9.3: nothing internal leaks into any surfaced text.
            var surfaced = $"{title}\n{detail}";
            Assert.False(ContainsCi(surfaced, t.secret),
                $"Leak-free violation: surfaced text reproduced secret '{t.secret}'.");
            Assert.DoesNotContain(".cs", surfaced, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" at ", surfaced, StringComparison.Ordinal);
            Assert.DoesNotContain(ex.GetType().Name, surfaced, StringComparison.Ordinal);
            // The raw thrown message must not be echoed verbatim for unmapped exceptions.
            Assert.DoesNotContain(t.message, surfaced, StringComparison.Ordinal);
        }, iter: Iterations);
    }

    // =====================================================================
    // Property 21: Validation errors report every failing check, keyed by field,
    //              and persist nothing
    // Validates: Requirements 9.4, 10.5
    // =====================================================================

    /// <summary>
    /// Feature: streaming-panel, Property 21: Validation errors report every failing check, keyed by field, and persist nothing
    ///
    /// For any non-empty set of FluentValidation failures — multiple fields, multiple messages per
    /// field — a thrown <see cref="ValidationException"/> yields a 400 Problem Details whose
    /// <c>errors</c> member is an object keyed by every failing field, where each field's array
    /// contains every one of its messages (R9.4). The response body is the only thing written: no
    /// success payload is produced, modelling "persists no part of the submitted DTO" at the
    /// middleware contract (R10.5).
    ///
    /// Validates: Requirements 9.4, 10.5
    /// </summary>
    [Fact]
    public void Property21_ValidationErrors_ReportEveryFailingCheckKeyedByField()
    {
        // Distinct field names so each becomes its own key; 1+ messages per field.
        var genField = Gen.String[Gen.Char.AlphaNumeric, 1, 20];
        var genMessage = Gen.String[Gen.Char.AlphaNumeric, 1, 60];

        var gen =
            from fieldCount in Gen.Int[1, 6]
            from fields in genField.Array[fieldCount, fieldCount]
            from msgCounts in Gen.Int[1, 4].Array[fieldCount, fieldCount]
            from messages in genMessage.Array[0, 60].Array[fieldCount, fieldCount]
            select (fields, msgCounts, messages);

        gen.Sample(t =>
        {
            // Build an expected map of distinct field -> list of messages, mirroring exactly what
            // we hand to the ValidationException so the assertion is self-consistent even when the
            // generator produces duplicate field names.
            var expected = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var failures = new List<ValidationFailure>();

            for (int i = 0; i < t.fields.Length; i++)
            {
                var field = t.fields[i];
                int count = t.msgCounts[i];
                var pool = t.messages[i];

                for (int m = 0; m < count; m++)
                {
                    // Make each message unique within the field so array membership is exact.
                    var msg = $"{(pool.Length > 0 ? pool[m % pool.Length] : "err")}#{i}.{m}";
                    failures.Add(new ValidationFailure(field, msg));

                    if (!expected.TryGetValue(field, out var list))
                    {
                        list = new List<string>();
                        expected[field] = list;
                    }
                    list.Add(msg);
                }
            }

            var exception = new ValidationException(failures);
            var (status, contentType, body) = RunAsync(Throwing(exception)).GetAwaiter().GetResult();
            using var bodyDoc = body;
            var root = bodyDoc.RootElement;

            Assert.Equal(ExceptionMiddleware.ProblemJsonContentType, contentType);
            Assert.Equal(StatusCodes.Status400BadRequest, status);

            Assert.True(root.TryGetProperty("status", out var statusEl));
            Assert.Equal(400, statusEl.GetInt32());

            // errors is an object keyed by field.
            Assert.True(root.TryGetProperty("errors", out var errorsEl));
            Assert.Equal(JsonValueKind.Object, errorsEl.ValueKind);

            // Every failing field is present as a key, carrying all of its messages.
            foreach (var (field, msgs) in expected)
            {
                Assert.True(errorsEl.TryGetProperty(field, out var arrEl),
                    $"Expected errors to contain key '{field}'.");
                Assert.Equal(JsonValueKind.Array, arrEl.ValueKind);

                var actual = arrEl.EnumerateArray().Select(e => e.GetString()).ToHashSet(StringComparer.Ordinal);
                foreach (var msg in msgs)
                {
                    Assert.Contains(msg, actual);
                }
            }

            // No extra fields appear beyond the ones that failed.
            var reportedKeys = errorsEl.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(expected.Keys.ToHashSet(StringComparer.Ordinal), reportedKeys);

            // "persist nothing": the only body is this error Problem Details — no success value member.
            Assert.False(root.TryGetProperty("value", out _));
            Assert.False(root.TryGetProperty("data", out _));
        }, iter: Iterations);
    }

    // =====================================================================
    // Property 22: Failed requests persist no partial data
    // Validates: Requirements 9.5, 12.5
    // =====================================================================

    /// <summary>
    /// Feature: streaming-panel, Property 22: Failed requests persist no partial data
    ///
    /// Whenever the downstream delegate throws — whether a validation failure, a typed
    /// <see cref="ResultException"/> (mapped to 404/409/401/403/503/400), or an unmapped exception
    /// (500) — the middleware short-circuits to an error Problem Details response and never emits a
    /// success body. This is the middleware-level contract that a failed request leaves no partial
    /// output behind (R9.5); the matching database rollback (R12.5) is enforced by the service unit
    /// of work and exercised in task 7.7.
    ///
    /// Validates: Requirements 9.5, 12.5
    /// </summary>
    [Fact]
    public void Property22_FailedRequests_PersistNoPartialData()
    {
        var mappedCodes = new[]
        {
            ErrorCode.Validation,
            ErrorCode.Unauthorized,
            ErrorCode.Forbidden,
            ErrorCode.NotFound,
            ErrorCode.Conflict,
            ErrorCode.Unavailable,
            ErrorCode.Internal,
        };

        var gen =
            // 0: validation exception, 1: result exception, 2: unmapped exception
            from flavour in Gen.Int[0, 2]
            from field in Gen.String[Gen.Char.AlphaNumeric, 1, 20]
            from message in Gen.String[Gen.Char.AlphaNumeric, 1, 40]
            from codeIndex in Gen.Int[0, mappedCodes.Length - 1]
            select (flavour, field, message, codeIndex);

        gen.Sample(t =>
        {
            Exception ex;
            int expectedStatus;

            switch (t.flavour)
            {
                case 0:
                    ex = new ValidationException(new[] { new ValidationFailure(t.field, t.message) });
                    expectedStatus = StatusCodes.Status400BadRequest;
                    break;
                case 1:
                    var code = mappedCodes[t.codeIndex];
                    ex = new ResultException(code, t.message);
                    expectedStatus = ErrorCodeHttpMapping.ToStatusCode(code);
                    break;
                default:
                    ex = new InvalidOperationException(t.message);
                    expectedStatus = StatusCodes.Status500InternalServerError;
                    break;
            }

            var (status, contentType, body) = RunAsync(Throwing(ex)).GetAwaiter().GetResult();
            using var bodyDoc = body;
            var root = bodyDoc.RootElement;

            // The error path always writes a Problem Details error response...
            Assert.Equal(ExceptionMiddleware.ProblemJsonContentType, contentType);
            Assert.Equal(expectedStatus, status);
            Assert.True(status >= 400, "A thrown request must surface a client/server error status.");

            // ...carrying the error status, and never a success payload.
            Assert.True(root.TryGetProperty("status", out var statusEl));
            Assert.Equal(expectedStatus, statusEl.GetInt32());
            Assert.False(root.TryGetProperty("value", out _),
                "A failed request must not emit a success value body.");
            Assert.False(root.TryGetProperty("data", out _),
                "A failed request must not emit a success data body.");
        }, iter: Iterations);
    }
}
