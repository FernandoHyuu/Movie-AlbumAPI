namespace StreamingPanel.Core.Dtos;

/// <summary>
/// A fetched cover image: the raw bytes plus the content type to send back as the
/// <c>Content-Type</c> header. The type is always one of image/jpeg, image/png, image/webp.
/// </summary>
public record CoverImage(byte[] Content, string ContentType);
