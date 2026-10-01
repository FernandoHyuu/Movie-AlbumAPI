namespace StreamingPanel.Core.Entities;

/// <summary>A music album, with its cover image bytes stored in-row.</summary>
public class Album
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public string? Band { get; set; }
    public int ReleaseYear { get; set; }
    public string? Genre { get; set; }
    public byte[]? CoverImageData { get; set; }
    public string? ContentType { get; set; }
}
