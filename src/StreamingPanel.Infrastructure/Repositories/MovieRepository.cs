using Microsoft.EntityFrameworkCore;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Interfaces;
using StreamingPanel.Infrastructure.Persistence;

namespace StreamingPanel.Infrastructure.Repositories;

/// <summary>
/// EF Core <see cref="IMovieRepository"/> over <see cref="AppDbContext"/>. List queries
/// project to a DTO with a computed <c>HasCover</c> flag and never pull the <c>bytea</c>
/// cover column — a listing could otherwise drag megabytes per row into memory. The bytes
/// are loaded only by <see cref="GetCoverAsync"/>, for the one requested row.
/// </summary>
public sealed class MovieRepository : IMovieRepository
{
    private readonly AppDbContext _db;

    public MovieRepository(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<MovieDto> Items, int TotalCount)> GetPagedAsync(int skip, int take)
    {
        // Count first, then project the page without CoverImageData.
        var totalCount = await _db.Movies.CountAsync();

        var items = await _db.Movies
            .AsNoTracking()
            .OrderBy(m => m.Title)
            .ThenBy(m => m.Id)
            .Skip(skip)
            .Take(take)
            .Select(m => new MovieDto(
                m.Id,
                m.Title,
                m.Studio,
                m.ReleaseYear,
                m.MainActors,
                m.CoverImageData != null))
            .ToListAsync();

        return (items, totalCount);
    }

    /// <inheritdoc />
    public Task<MovieDto?> GetByIdAsync(Guid id) =>
        _db.Movies
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new MovieDto(
                m.Id,
                m.Title,
                m.Studio,
                m.ReleaseYear,
                m.MainActors,
                m.CoverImageData != null))
            .SingleOrDefaultAsync();

    /// <inheritdoc />
    public Task<Movie?> GetEntityAsync(Guid id) =>
        _db.Movies.SingleOrDefaultAsync(m => m.Id == id);

    /// <inheritdoc />
    public async Task<CoverImage?> GetCoverAsync(Guid id)
    {
        // Pull only the cover bytes and content type for this one row.
        var row = await _db.Movies
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new { m.CoverImageData, m.ContentType })
            .SingleOrDefaultAsync();

        if (row is null || row.CoverImageData is null || row.ContentType is null)
        {
            return null;
        }

        return new CoverImage(row.CoverImageData, row.ContentType);
    }

    /// <inheritdoc />
    public async Task AddAsync(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        await _db.Movies.AddAsync(movie);
    }

    /// <inheritdoc />
    public void Update(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        _db.Movies.Update(movie);
    }

    /// <inheritdoc />
    public void Remove(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        _db.Movies.Remove(movie);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
