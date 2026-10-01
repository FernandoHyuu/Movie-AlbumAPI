namespace StreamingPanel.Core.Dtos;

/// <summary>
/// Payload for creating or updating a Movie. Cover bytes travel separately as
/// multipart/form-data, not in this DTO.
/// </summary>
public record MovieWriteDto(string Title, string? Studio, int ReleaseYear, List<string> MainActors);

/// <summary>
/// Movie as returned by read and list endpoints. <see cref="HasCover"/> is just a flag;
/// the actual cover bytes are only ever served by the dedicated cover endpoint.
/// </summary>
public record MovieDto(
    Guid Id,
    string Title,
    string? Studio,
    int ReleaseYear,
    List<string> MainActors,
    bool HasCover);
