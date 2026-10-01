using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Interfaces;

namespace StreamingPanel.Infrastructure.Services;

/// <summary>
/// Movie catalog business rules over <see cref="IMovieRepository"/>: paging with a clamped
/// page size, 404s for unknown ids, and cover upload/fetch.
/// </summary>
public sealed class MovieService : IMovieService
{
    /// <summary>Per-page cap for the Movie catalog.</summary>
    public const int MaxPageSize = 100;

    private readonly IMovieRepository _movies;

    public MovieService(IMovieRepository movies)
    {
        _movies = movies ?? throw new ArgumentNullException(nameof(movies));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<MovieDto>>> GetPagedAsync(int pageNumber, int pageSize)
    {
        // Floor the page to 1 and clamp the size into 1..MaxPageSize so a request can't
        // exceed the cap or produce a negative offset.
        var page = pageNumber < 1 ? 1 : pageNumber;
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var skip = (page - 1) * size;

        var (items, totalCount) = await _movies.GetPagedAsync(skip, size);

        var envelope = new PagedResult<MovieDto>(items, page, size, totalCount);
        return Result.Success(envelope);
    }

    /// <inheritdoc />
    public async Task<Result<MovieDto>> GetByIdAsync(Guid id)
    {
        var dto = await _movies.GetByIdAsync(id);
        return dto is null
            ? Result.Failure<MovieDto>(ErrorCode.NotFound, "The requested movie was not found.")
            : Result.Success(dto);
    }

    /// <inheritdoc />
    public async Task<Result<MovieDto>> CreateAsync(MovieWriteDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Title = dto.Title,
            Studio = dto.Studio,
            ReleaseYear = dto.ReleaseYear,
            MainActors = dto.MainActors ?? new List<string>(),
        };

        await _movies.AddAsync(movie);
        await _movies.SaveChangesAsync();

        return Result.Success(ToDto(movie));
    }

    /// <inheritdoc />
    public async Task<Result<MovieDto>> UpdateAsync(Guid id, MovieWriteDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var movie = await _movies.GetEntityAsync(id);
        if (movie is null)
        {
            return Result.Failure<MovieDto>(ErrorCode.NotFound, "The requested movie was not found.");
        }

        // Update display fields only. Cover bytes belong to the cover endpoints and a
        // write-DTO update deliberately leaves them alone.
        movie.Title = dto.Title;
        movie.Studio = dto.Studio;
        movie.ReleaseYear = dto.ReleaseYear;
        movie.MainActors = dto.MainActors ?? new List<string>();

        _movies.Update(movie);
        await _movies.SaveChangesAsync();

        return Result.Success(ToDto(movie));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id)
    {
        var movie = await _movies.GetEntityAsync(id);
        if (movie is null)
        {
            return Result.Failure(ErrorCode.NotFound, "The requested movie was not found.");
        }

        _movies.Remove(movie);
        await _movies.SaveChangesAsync();

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<CoverImage>> GetCoverAsync(Guid id)
    {
        // The repository returns null both for a missing movie and for one with no stored
        // cover. Check existence so the two 404s carry the right reason.
        var cover = await _movies.GetCoverAsync(id);
        if (cover is not null)
        {
            return Result.Success(cover);
        }

        var exists = await _movies.GetByIdAsync(id) is not null;
        return exists
            ? Result.Failure<CoverImage>(ErrorCode.NotFound, "No cover image exists for this movie.")
            : Result.Failure<CoverImage>(ErrorCode.NotFound, "The requested movie was not found.");
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

        var movie = await _movies.GetEntityAsync(id);
        if (movie is null)
        {
            return Result.Failure(ErrorCode.NotFound, "The requested movie was not found.");
        }

        movie.CoverImageData = bytes;
        movie.ContentType = contentType;

        _movies.Update(movie);
        await _movies.SaveChangesAsync();

        return Result.Success();
    }

    /// <summary>Projects a tracked <see cref="Movie"/> entity to its read DTO.</summary>
    private static MovieDto ToDto(Movie movie) => new(
        movie.Id,
        movie.Title,
        movie.Studio,
        movie.ReleaseYear,
        movie.MainActors,
        movie.CoverImageData != null);
}
