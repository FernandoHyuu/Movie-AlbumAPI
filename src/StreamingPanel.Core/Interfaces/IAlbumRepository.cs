using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;

namespace StreamingPanel.Core.Interfaces;

/// <summary>
/// Persistence boundary for the <see cref="Album"/> aggregate. List and read
/// projections must never load the heavy <see cref="Album.CoverImageData"/> bytes;
/// those are read one row at a time through <see cref="GetCoverAsync"/>. Mutations
/// are staged on the tracked context and only hit the database on
/// <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IAlbumRepository
{
    /// <summary>
    /// Returns one page of albums ordered by title plus the total matching count.
    /// Cover bytes are not loaded.
    /// </summary>
    Task<(IReadOnlyList<AlbumDto> Items, int TotalCount)> GetPagedAsync(int skip, int take);

    /// <summary>Returns the album as an <see cref="AlbumDto"/>, or null if not found. Cover bytes are not loaded.</summary>
    Task<AlbumDto?> GetByIdAsync(Guid id);

    /// <summary>Returns the tracked entity for updates/deletes, or null if not found.</summary>
    Task<Album?> GetEntityAsync(Guid id);

    /// <summary>
    /// Reads only the cover bytes and content type for one row. Returns null when the
    /// row is missing or has no stored image.
    /// </summary>
    Task<CoverImage?> GetCoverAsync(Guid id);

    Task AddAsync(Album album);

    void Update(Album album);

    void Remove(Album album);

    Task SaveChangesAsync();
}
