using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Core.Interfaces;

/// <summary>
/// Business rules for the Album catalog. Reads, writes, and deletes return
/// <see cref="ErrorCode.NotFound"/> for an unknown id. Cover storage accepts images up
/// to 5 MB of type image/jpeg, image/png, or image/webp. Expected failures come back as
/// a failed <see cref="Result{T}"/>.
/// </summary>
public interface IAlbumService
{
    /// <summary>
    /// Returns one page of albums. <paramref name="pageNumber"/> is floored to 1 and
    /// <paramref name="pageSize"/> is clamped to 1..50.
    /// </summary>
    Task<Result<PagedResult<AlbumDto>>> GetPagedAsync(int pageNumber, int pageSize);

    /// <summary>Returns the album, or <see cref="ErrorCode.NotFound"/> when the id is unknown.</summary>
    Task<Result<AlbumDto>> GetByIdAsync(Guid id);

    /// <summary>Creates an album and returns the stored representation.</summary>
    Task<Result<AlbumDto>> CreateAsync(AlbumWriteDto dto);

    /// <summary>Updates the album, or returns <see cref="ErrorCode.NotFound"/> when the id is unknown.</summary>
    Task<Result<AlbumDto>> UpdateAsync(Guid id, AlbumWriteDto dto);

    /// <summary>Deletes the album, or returns <see cref="ErrorCode.NotFound"/> when the id is unknown.</summary>
    Task<Result> DeleteAsync(Guid id);

    /// <summary>
    /// Returns the stored cover, or <see cref="ErrorCode.NotFound"/> when the album does
    /// not exist or has no stored image.
    /// </summary>
    Task<Result<CoverImage>> GetCoverAsync(Guid id);

    /// <summary>
    /// Stores or replaces the cover. Rejects payloads over 5 MB or with an unsupported
    /// content type via <see cref="ErrorCode.Validation"/> without touching stored data,
    /// and returns <see cref="ErrorCode.NotFound"/> when the album does not exist.
    /// </summary>
    Task<Result> StoreCoverAsync(Guid id, byte[] bytes, string contentType);
}
