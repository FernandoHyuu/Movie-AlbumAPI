using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Interfaces;

namespace StreamingPanel.Infrastructure.Services;

/// <summary>
/// Album catalog business rules over <see cref="IAlbumRepository"/>: paging with a clamped
/// page size, 404s for unknown ids, and cover upload/fetch.
/// </summary>
public sealed class AlbumService : IAlbumService
{
    /// <summary>Per-page cap for the Album catalog.</summary>
    public const int MaxPageSize = 50;

    private readonly IAlbumRepository _albums;

    public AlbumService(IAlbumRepository albums)
    {
        _albums = albums ?? throw new ArgumentNullException(nameof(albums));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<AlbumDto>>> GetPagedAsync(int pageNumber, int pageSize)
    {
        // Floor the page to 1 and clamp the size into 1..MaxPageSize so a request can't
        // exceed the cap or produce a negative offset.
        var page = pageNumber < 1 ? 1 : pageNumber;
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var skip = (page - 1) * size;

        var (items, totalCount) = await _albums.GetPagedAsync(skip, size);

        var envelope = new PagedResult<AlbumDto>(items, page, size, totalCount);
        return Result.Success(envelope);
    }

    /// <inheritdoc />
    public async Task<Result<AlbumDto>> GetByIdAsync(Guid id)
    {
        var dto = await _albums.GetByIdAsync(id);
        return dto is null
            ? Result.Failure<AlbumDto>(ErrorCode.NotFound, "The requested album was not found.")
            : Result.Success(dto);
    }

    /// <inheritdoc />
    public async Task<Result<AlbumDto>> CreateAsync(AlbumWriteDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var album = new Album
        {
            Id = Guid.NewGuid(),
            Title = dto.Title,
            Band = dto.Band,
            ReleaseYear = dto.ReleaseYear,
            Genre = dto.Genre,
        };

        await _albums.AddAsync(album);
        await _albums.SaveChangesAsync();

        return Result.Success(ToDto(album));
    }

    /// <inheritdoc />
    public async Task<Result<AlbumDto>> UpdateAsync(Guid id, AlbumWriteDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var album = await _albums.GetEntityAsync(id);
        if (album is null)
        {
            return Result.Failure<AlbumDto>(ErrorCode.NotFound, "The requested album was not found.");
        }

        // Update display fields only. Cover bytes belong to the cover endpoints and a
        // write-DTO update deliberately leaves them alone.
        album.Title = dto.Title;
        album.Band = dto.Band;
        album.ReleaseYear = dto.ReleaseYear;
        album.Genre = dto.Genre;

        _albums.Update(album);
        await _albums.SaveChangesAsync();

        return Result.Success(ToDto(album));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id)
    {
        var album = await _albums.GetEntityAsync(id);
        if (album is null)
        {
            return Result.Failure(ErrorCode.NotFound, "The requested album was not found.");
        }

        _albums.Remove(album);
        await _albums.SaveChangesAsync();

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<CoverImage>> GetCoverAsync(Guid id)
    {
        // The repository returns null both for a missing album and for one with no stored
        // cover. Check existence so the two 404s carry the right reason.
        var cover = await _albums.GetCoverAsync(id);
        if (cover is not null)
        {
            return Result.Success(cover);
        }

        var exists = await _albums.GetByIdAsync(id) is not null;
        return exists
            ? Result.Failure<CoverImage>(ErrorCode.NotFound, "No cover image exists for this album.")
            : Result.Failure<CoverImage>(ErrorCode.NotFound, "The requested album was not found.");
    }

    /// <inheritdoc />
    public async Task<Result> StoreCoverAsync(Guid id, byte[] bytes, string contentType)
    {
        // Validate before loading or touching the record so a rejected upload leaves
        // stored data untouched.
        var validation = CoverImageRules.Validate(bytes, contentType);
        if (validation.IsFailure)
        {
            return validation;
        }

        var album = await _albums.GetEntityAsync(id);
        if (album is null)
        {
            return Result.Failure(ErrorCode.NotFound, "The requested album was not found.");
        }

        album.CoverImageData = bytes;
        album.ContentType = contentType;

        _albums.Update(album);
        await _albums.SaveChangesAsync();

        return Result.Success();
    }

    /// <summary>Projects a tracked <see cref="Album"/> entity to its read DTO.</summary>
    private static AlbumDto ToDto(Album album) => new(
        album.Id,
        album.Title,
        album.Band,
        album.ReleaseYear,
        album.Genre,
        album.CoverImageData != null);
}
