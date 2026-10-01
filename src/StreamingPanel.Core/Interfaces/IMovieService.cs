using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Core.Interfaces;

/// <summary>
/// Business rules for the Movie catalog. Reads, writes, and deletes return
/// <see cref="ErrorCode.NotFound"/> for an unknown id. Cover storage accepts images up
/// to 5 MB of type image/jpeg, image/png, or image/webp. Expected failures come back as
/// a failed <see cref="Result{T}"/>.
/// </summary>
public interface IMovieService
{
    /// <summary>
    /// Returns one page of movies. <paramref name="pageNumber"/> is floored to 1 and
    /// <paramref name="pageSize"/> is clamped to 1..100.
    /// </summary>
    Task<Result<PagedResult<MovieDto>>> GetPagedAsync(int pageNumber, int pageSize);

    /// <summary>Returns the movie, or <see cref="ErrorCode.NotFound"/> when the id is unknown.</summary>
    Task<Result<MovieDto>> GetByIdAsync(Guid id);

    /// <summary>Creates a movie and returns the stored representation.</summary>
    Task<Result<MovieDto>> CreateAsync(MovieWriteDto dto);

    /// <summary>Updates the movie, or returns <see cref="ErrorCode.NotFound"/> when the id is unknown.</summary>
    Task<Result<MovieDto>> UpdateAsync(Guid id, MovieWriteDto dto);

    /// <summary>Deletes the movie, or returns <see cref="ErrorCode.NotFound"/> when the id is unknown.</summary>
    Task<Result> DeleteAsync(Guid id);

    /// <summary>
    /// Returns the stored cover, or <see cref="ErrorCode.NotFound"/> when the movie does
    /// not exist or has no stored image.
    /// </summary>
    Task<Result<CoverImage>> GetCoverAsync(Guid id);

    /// <summary>
    /// Stores or replaces the cover. Rejects payloads over 5 MB or with an unsupported
    /// content type via <see cref="ErrorCode.Validation"/> without touching stored data,
    /// and returns <see cref="ErrorCode.NotFound"/> when the movie does not exist.
    /// </summary>
    Task<Result> StoreCoverAsync(Guid id, byte[] bytes, string contentType);
}
