using Microsoft.EntityFrameworkCore;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Interfaces;
using StreamingPanel.Infrastructure.Persistence;

namespace StreamingPanel.Infrastructure.Repositories;

/// <summary>
/// EF Core <see cref="IAlbumRepository"/> over <see cref="AppDbContext"/>. List queries
/// project to a DTO with a computed <c>HasCover</c> flag and never pull the <c>bytea</c>
/// cover column — a listing could otherwise drag megabytes per row into memory. The bytes
/// are loaded only by <see cref="GetCoverAsync"/>, for the one requested row.
/// </summary>
public sealed class AlbumRepository : IAlbumRepository
{
    private readonly AppDbContext _db;

    public AlbumRepository(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<AlbumDto> Items, int TotalCount)> GetPagedAsync(int skip, int take)
    {
        // Count first, then project the page without CoverImageData.
        var totalCount = await _db.Albums.CountAsync();

        var items = await _db.Albums
            .AsNoTracking()
            .OrderBy(a => a.Title)
            .ThenBy(a => a.Id)
            .Skip(skip)
            .Take(take)
            .Select(a => new AlbumDto(
                a.Id,
                a.Title,
                a.Band,
                a.ReleaseYear,
                a.Genre,
                a.CoverImageData != null))
            .ToListAsync();

        return (items, totalCount);
    }

    /// <inheritdoc />
    public Task<AlbumDto?> GetByIdAsync(Guid id) =>
        _db.Albums
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AlbumDto(
                a.Id,
                a.Title,
                a.Band,
                a.ReleaseYear,
                a.Genre,
                a.CoverImageData != null))
            .SingleOrDefaultAsync();

    /// <inheritdoc />
    public Task<Album?> GetEntityAsync(Guid id) =>
        _db.Albums.SingleOrDefaultAsync(a => a.Id == id);

    /// <inheritdoc />
    public async Task<CoverImage?> GetCoverAsync(Guid id)
    {
        // Pull only the cover bytes and content type for this one row.
        var row = await _db.Albums
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new { a.CoverImageData, a.ContentType })
            .SingleOrDefaultAsync();

        if (row is null || row.CoverImageData is null || row.ContentType is null)
        {
            return null;
        }

        return new CoverImage(row.CoverImageData, row.ContentType);
    }

    /// <inheritdoc />
    public async Task AddAsync(Album album)
    {
        ArgumentNullException.ThrowIfNull(album);
        await _db.Albums.AddAsync(album);
    }

    /// <inheritdoc />
    public void Update(Album album)
    {
        ArgumentNullException.ThrowIfNull(album);
        _db.Albums.Update(album);
    }

    /// <inheritdoc />
    public void Remove(Album album)
    {
        ArgumentNullException.ThrowIfNull(album);
        _db.Albums.Remove(album);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
