namespace StreamingPanel.Core.Dtos;

/// <summary>
/// Payload for creating or updating an Album. Cover bytes travel separately as
/// multipart/form-data, not in this DTO.
/// </summary>
public record AlbumWriteDto(string Title, string? Band, int ReleaseYear, string? Genre);

/// <summary>
/// Album as returned by read and list endpoints. <see cref="HasCover"/> is just a flag;
/// the actual cover bytes are only ever served by the dedicated cover endpoint.
/// </summary>
public record AlbumDto(
    Guid Id,
    string Title,
    string? Band,
    int ReleaseYear,
    string? Genre,
    bool HasCover);
