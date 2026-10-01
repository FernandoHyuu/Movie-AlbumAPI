namespace StreamingPanel.Core.Entities;

/// <summary>A film, with its cover image bytes stored in-row.</summary>
public class Movie
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public string? Studio { get; set; }
    public int ReleaseYear { get; set; }

    // Persisted as a PostgreSQL text[].
    public List<string> MainActors { get; set; } = new();

    public byte[]? CoverImageData { get; set; }
    public string? ContentType { get; set; }
}
