using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Infrastructure.Services;

/// <summary>
/// Shared cover-upload validation used by <see cref="MovieService"/> and
/// <see cref="AlbumService"/>: accept only an allowed content type within the size cap.
/// </summary>
internal static class CoverImageRules
{
    /// <summary>Maximum accepted cover size, 5 MB.</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>Accepted cover content types.</summary>
    public static readonly IReadOnlySet<string> AllowedContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp",
        };

    /// <summary>
    /// Returns success when the upload is within the size cap and an allowed content type,
    /// otherwise a validation failure describing the first offending constraint.
    /// </summary>
    public static Result Validate(byte[]? bytes, string? contentType)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return Result.Failure(ErrorCode.Validation, "The cover image payload is empty.");
        }

        if (bytes.Length > MaxBytes)
        {
            return Result.Failure(
                ErrorCode.Validation,
                $"The cover image exceeds the maximum size of {MaxBytes} bytes (5 MB).");
        }

        if (string.IsNullOrWhiteSpace(contentType)
            || !AllowedContentTypes.Contains(contentType))
        {
            return Result.Failure(
                ErrorCode.Validation,
                "The cover image content type must be one of image/jpeg, image/png, or image/webp.");
        }

        return Result.Success();
    }
}
