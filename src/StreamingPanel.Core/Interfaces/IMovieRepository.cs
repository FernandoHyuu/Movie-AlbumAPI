using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;

namespace StreamingPanel.Core.Interfaces;

/// <summary>
/// Persistence boundary for the <see cref="Movie"/> aggregate. List and read
/// projections must never load the heavy <see cref="Movie.CoverImageData"/> bytes;
/// those are read one row at a time through <see cref="GetCoverAsync"/>. Mutations
/// are staged on the tracked context and only hit the database on
/// <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IMovieRepository
{
    /// <summary>
    /// Returns one page of movies ordered by title plus the total matching count.
    /// Cover bytes are not loaded.
    /// </summary>
    Task<(IReadOnlyList<MovieDto> Items, int TotalCount)> GetPagedAsync(int skip, int take);

    /// <summary>Returns the movie as a <see cref="MovieDto"/>, or null if not found. Cover bytes are not loaded.</summary>
    Task<MovieDto?> GetByIdAsync(Guid id);

    /// <summary>Returns the tracked entity for updates/deletes, or null if not found.</summary>
    Task<Movie?> GetEntityAsync(Guid id);

    /// <summary>
    /// Reads only the cover bytes and content type for one row. Returns null when the
    /// row is missing or has no stored image.
    /// </summary>
    Task<CoverImage?> GetCoverAsync(Guid id);

    Task AddAsync(Movie movie);

    void Update(Movie movie);

    void Remove(Movie movie);

    Task SaveChangesAsync();
}
