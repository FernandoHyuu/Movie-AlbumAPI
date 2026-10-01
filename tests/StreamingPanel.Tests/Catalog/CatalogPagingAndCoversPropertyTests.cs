using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;
using StreamingPanel.Infrastructure.Persistence;
using StreamingPanel.Infrastructure.Repositories;
using StreamingPanel.Infrastructure.Services;

namespace StreamingPanel.Tests.Catalog;

/// <summary>
/// Property-based tests for the Movie/Album catalog paging and cover store/fetch flows,
/// exercising <see cref="MovieService"/> and <see cref="AlbumService"/> over the real
/// <see cref="MovieRepository"/>/<see cref="AlbumRepository"/> and <see cref="AppDbContext"/>.
///
/// Feature: streaming-panel, Property 17: Catalog pages never exceed their cap and carry required fields
/// Feature: streaming-panel, Property 18: Cover image store/fetch is a faithful round-trip
/// Feature: streaming-panel, Property 19: Oversized or wrong-type covers are rejected without modifying stored data
///
/// Validates: Requirements 6.1, 7.1, 7.2, 8.1, 8.2, 8.3, 20.1
///
/// Database provider: Microsoft.EntityFrameworkCore.Sqlite (in-memory). SQLite is used
/// instead of the EF Core InMemory provider so the real services, repositories, and
/// entity configurations run unchanged against a relational store. The production
/// <see cref="Movie"/> mapping stores <c>MainActors</c> as a PostgreSQL <c>text[]</c>,
/// which SQLite cannot represent natively; the test-only <see cref="SqliteCatalogDbContext"/>
/// supplies a JSON value converter for that one property (mirrors the Security/Persistence
/// test harnesses). Each sample runs against a fresh in-memory database for isolation;
/// every property executes a minimum of 100 iterations.
/// </summary>
public class CatalogPagingAndCoversPropertyTests
{
    private const int MovieCap = MovieService.MaxPageSize; // 100 (R6.1)
    private const int AlbumCap = AlbumService.MaxPageSize;  // 50 (R7.1, R7.2)

    // Keep cover payloads small for speed; the oversized path uses a dedicated boundary buffer.
    private const int MaxValidCoverBytes = 5 * 1024 * 1024; // 5 MB (R8.2)

    private static readonly string[] ValidContentTypes = { "image/jpeg", "image/png", "image/webp" };

    private static readonly string[] InvalidContentTypes =
    {
        "image/gif", "image/bmp", "image/svg+xml", "application/pdf", "application/octet-stream",
        "text/plain", "video/mp4", "image/tiff", "", " ",
    };

    private static readonly Gen<string> GenTitle =
        Gen.Char['a', 'z'].Array[1, 20].Select(cs => new string(cs));

    private static readonly Gen<int> GenReleaseYear = Gen.Int[1888, 2100];

    private static readonly Gen<string?> GenOptionalText =
        Gen.Char['a', 'z'].Array[0, 15].Select(cs => cs.Length == 0 ? null : new string(cs));

    private static readonly Gen<List<string>> GenMainActors =
        Gen.Char['a', 'z'].Array[1, 12]
            .Select(cs => new string(cs))
            .Array[0, 5]
            .Select(a => a.ToList());

    /// <summary>Small, non-empty valid cover payloads (a few KB) for round-trip speed.</summary>
    private static readonly Gen<byte[]> GenValidCoverBytes =
        Gen.Byte.Array[1, 4096];

    private static readonly Gen<string> GenValidContentType = Gen.OneOfConst(ValidContentTypes);

    private static readonly Gen<string> GenInvalidContentType = Gen.OneOfConst(InvalidContentTypes);

    /// <summary>
    /// Property 17 — Catalog pages never exceed their cap and carry required fields.
    ///
    /// Seed a random number of movies (and separately albums), then request a page with a
    /// random page size (including values above the cap and ≤0). Assert: the returned
    /// Items count is ≤ the effective cap and ≤ the total seeded; the envelope's pageSize
    /// is clamped into 1..cap; totalCount equals the seeded total; and every returned DTO
    /// carries its required display fields (Movie: Title, Studio nullable, ReleaseYear,
    /// MainActors non-null, HasCover; Album: Title, Band nullable, ReleaseYear, Genre
    /// nullable, HasCover).
    ///
    /// Feature: streaming-panel, Property 17: Catalog pages never exceed their cap and carry required fields
    /// Validates: Requirements 6.1, 7.1, 7.2, 20.1
    /// </summary>
    [Fact]
    public void CatalogPagesNeverExceedTheirCapAndCarryRequiredFields()
    {
        // pageSize deliberately spans ≤0, in-range, and above-cap values.
        var genPageSize = Gen.OneOf(
            Gen.Int[-5, 0],
            Gen.Int[1, AlbumCap],
            Gen.Int[AlbumCap + 1, MovieCap],
            Gen.Int[MovieCap + 1, MovieCap + 500]);

        (from movieCount in Gen.Int[0, 8]
         from albumCount in Gen.Int[0, 8]
         from pageSize in genPageSize
         from pageNumber in Gen.Int[-2, 3]
         select (movieCount, albumCount, pageSize, pageNumber))
            .Sample(
                input =>
                {
                    var (movieCount, albumCount, pageSize, pageNumber) = input;

                    using var connection = OpenDatabase();
                    var options = BuildOptions(connection);

                    SeedMovies(options, movieCount);
                    SeedAlbums(options, albumCount);

                    Result<PagedResult<MovieDto>> movieResult;
                    Result<PagedResult<AlbumDto>> albumResult;
                    using (var ctx = new SqliteCatalogDbContext(options))
                    {
                        var movieService = new MovieService(new MovieRepository(ctx));
                        var albumService = new AlbumService(new AlbumRepository(ctx));
                        movieResult = movieService.GetPagedAsync(pageNumber, pageSize).GetAwaiter().GetResult();
                        albumResult = albumService.GetPagedAsync(pageNumber, pageSize).GetAwaiter().GetResult();
                    }

                    if (!movieResult.IsSuccess || !albumResult.IsSuccess)
                    {
                        return false;
                    }

                    var movies = movieResult.Value;
                    var albums = albumResult.Value;

                    // Envelope page size is clamped into 1..cap (R6.1, R7.1/7.2).
                    var movieSizeClamped = movies.PageSize >= 1 && movies.PageSize <= MovieCap;
                    var albumSizeClamped = albums.PageSize >= 1 && albums.PageSize <= AlbumCap;

                    // totalCount reflects the seeded total (R20.1 envelope semantics).
                    var movieTotalOk = movies.TotalCount == movieCount;
                    var albumTotalOk = albums.TotalCount == albumCount;

                    // Items count never exceeds the cap, the effective page size, or the total.
                    var movieCountOk = movies.Items.Count <= Math.Min(movies.PageSize, movieCount)
                        && movies.Items.Count <= MovieCap;
                    var albumCountOk = albums.Items.Count <= Math.Min(albums.PageSize, albumCount)
                        && albums.Items.Count <= AlbumCap;

                    // Every returned DTO carries the required display fields.
                    var movieFieldsOk = movies.Items.All(m =>
                        !string.IsNullOrEmpty(m.Title)
                        && m.MainActors != null
                        && m.ReleaseYear >= 1888 && m.ReleaseYear <= 2100
                        && m.Id != Guid.Empty);

                    var albumFieldsOk = albums.Items.All(a =>
                        !string.IsNullOrEmpty(a.Title)
                        && a.ReleaseYear >= 1888 && a.ReleaseYear <= 2100
                        && a.Id != Guid.Empty);

                    return movieSizeClamped && albumSizeClamped
                        && movieTotalOk && albumTotalOk
                        && movieCountOk && albumCountOk
                        && movieFieldsOk && albumFieldsOk;
                },
                iter: 100);
    }

    /// <summary>
    /// Property 18 — Cover image store/fetch is a faithful round-trip.
    ///
    /// Create a movie (and separately an album), store a random valid cover (small bytes +
    /// a valid content type), then fetch it back. Assert the fetched bytes are byte-for-byte
    /// identical (SequenceEqual) and the Content-Type is unchanged (R8.1, R8.3).
    ///
    /// Feature: streaming-panel, Property 18: Cover image store/fetch is a faithful round-trip
    /// Validates: Requirements 8.1, 8.3
    /// </summary>
    [Fact]
    public void CoverImageStoreFetchIsAFaithfulRoundTrip()
    {
        (from bytes in GenValidCoverBytes
         from contentType in GenValidContentType
         select (bytes, contentType))
            .Sample(
                input =>
                {
                    var (bytes, contentType) = input;

                    using var connection = OpenDatabase();
                    var options = BuildOptions(connection);

                    var movieId = SeedMovies(options, 1).Single();
                    var albumId = SeedAlbums(options, 1).Single();

                    Result movieStore, albumStore;
                    Result<CoverImage> movieFetch, albumFetch;
                    using (var ctx = new SqliteCatalogDbContext(options))
                    {
                        var movieService = new MovieService(new MovieRepository(ctx));
                        var albumService = new AlbumService(new AlbumRepository(ctx));

                        movieStore = movieService.StoreCoverAsync(movieId, bytes, contentType).GetAwaiter().GetResult();
                        albumStore = albumService.StoreCoverAsync(albumId, bytes, contentType).GetAwaiter().GetResult();
                    }

                    using (var ctx = new SqliteCatalogDbContext(options))
                    {
                        var movieService = new MovieService(new MovieRepository(ctx));
                        var albumService = new AlbumService(new AlbumRepository(ctx));

                        movieFetch = movieService.GetCoverAsync(movieId).GetAwaiter().GetResult();
                        albumFetch = albumService.GetCoverAsync(albumId).GetAwaiter().GetResult();
                    }

                    if (!movieStore.IsSuccess || !albumStore.IsSuccess
                        || !movieFetch.IsSuccess || !albumFetch.IsSuccess)
                    {
                        return false;
                    }

                    var movieRoundTrips = movieFetch.Value.Content.SequenceEqual(bytes)
                        && movieFetch.Value.ContentType == contentType;
                    var albumRoundTrips = albumFetch.Value.Content.SequenceEqual(bytes)
                        && albumFetch.Value.ContentType == contentType;

                    return movieRoundTrips && albumRoundTrips;
                },
                iter: 100);
    }

    /// <summary>
    /// Property 19 — Oversized or wrong-type covers are rejected without modifying stored data.
    ///
    /// Seed a known-good cover first, then attempt a store that must be rejected: either a
    /// disallowed content type (common path) or oversized bytes (>5 MB, boundary path). Assert
    /// the store returns a <see cref="ErrorCode.Validation"/> failure AND the previously stored
    /// cover bytes/type are unchanged when fetched back (R8.2, R20.6).
    ///
    /// Feature: streaming-panel, Property 19: Oversized or wrong-type covers are rejected without modifying stored data
    /// Validates: Requirements 8.2, 20.6
    /// </summary>
    [Fact]
    public void OversizedOrWrongTypeCoversAreRejectedWithoutModifyingStoredData()
    {
        // Favor the (cheap) content-type rejection path; sample the (expensive) oversized
        // boundary path less often. useOversized=true ~1 in 5.
        var genRejection =
            from goodBytes in GenValidCoverBytes
            from goodType in GenValidContentType
            from useOversized in Gen.Int[0, 4].Select(n => n == 0)
            from badType in GenInvalidContentType
            select (goodBytes, goodType, useOversized, badType);

        genRejection.Sample(
            input =>
            {
                var (goodBytes, goodType, useOversized, badType) = input;

                using var connection = OpenDatabase();
                var options = BuildOptions(connection);

                var movieId = SeedMovies(options, 1).Single();
                var albumId = SeedAlbums(options, 1).Single();

                // Seed a known-good cover on both records.
                using (var ctx = new SqliteCatalogDbContext(options))
                {
                    var movieService = new MovieService(new MovieRepository(ctx));
                    var albumService = new AlbumService(new AlbumRepository(ctx));
                    var m = movieService.StoreCoverAsync(movieId, goodBytes, goodType).GetAwaiter().GetResult();
                    var a = albumService.StoreCoverAsync(albumId, goodBytes, goodType).GetAwaiter().GetResult();
                    if (!m.IsSuccess || !a.IsSuccess)
                    {
                        return false;
                    }
                }

                // Build the rejected upload: oversized valid-type bytes OR valid-size invalid-type.
                byte[] badBytes;
                string badContentType;
                if (useOversized)
                {
                    badBytes = new byte[MaxValidCoverBytes + 1]; // just over the 5 MB cap
                    badContentType = goodType;                   // valid type, oversized bytes
                }
                else
                {
                    badBytes = goodBytes;                        // valid size
                    badContentType = badType;                    // disallowed type
                }

                Result movieReject, albumReject;
                using (var ctx = new SqliteCatalogDbContext(options))
                {
                    var movieService = new MovieService(new MovieRepository(ctx));
                    var albumService = new AlbumService(new AlbumRepository(ctx));
                    movieReject = movieService.StoreCoverAsync(movieId, badBytes, badContentType).GetAwaiter().GetResult();
                    albumReject = albumService.StoreCoverAsync(albumId, badBytes, badContentType).GetAwaiter().GetResult();
                }

                // Both rejections must be Validation failures.
                if (movieReject.IsSuccess || movieReject.ErrorCode != ErrorCode.Validation
                    || albumReject.IsSuccess || albumReject.ErrorCode != ErrorCode.Validation)
                {
                    return false;
                }

                // The stored cover must be unchanged.
                Result<CoverImage> movieFetch, albumFetch;
                using (var ctx = new SqliteCatalogDbContext(options))
                {
                    var movieService = new MovieService(new MovieRepository(ctx));
                    var albumService = new AlbumService(new AlbumRepository(ctx));
                    movieFetch = movieService.GetCoverAsync(movieId).GetAwaiter().GetResult();
                    albumFetch = albumService.GetCoverAsync(albumId).GetAwaiter().GetResult();
                }

                if (!movieFetch.IsSuccess || !albumFetch.IsSuccess)
                {
                    return false;
                }

                var movieUnchanged = movieFetch.Value.Content.SequenceEqual(goodBytes)
                    && movieFetch.Value.ContentType == goodType;
                var albumUnchanged = albumFetch.Value.Content.SequenceEqual(goodBytes)
                    && albumFetch.Value.ContentType == goodType;

                return movieUnchanged && albumUnchanged;
            },
            iter: 100);
    }

    // ---- Shared test harness -------------------------------------------------

    private static SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = BuildOptions(connection);
        using var ctx = new SqliteCatalogDbContext(options);
        ctx.Database.EnsureCreated();

        return connection;
    }

    private static DbContextOptions<AppDbContext> BuildOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

    private static List<Guid> SeedMovies(DbContextOptions<AppDbContext> options, int count)
    {
        var ids = new List<Guid>(count);
        using var ctx = new SqliteCatalogDbContext(options);
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            ctx.Movies.Add(new Movie
            {
                Id = id,
                Title = $"movie-{i}-{id:N}".Substring(0, Math.Min(30, $"movie-{i}-{id:N}".Length)),
                Studio = i % 2 == 0 ? $"studio-{i}" : null,
                ReleaseYear = 1990 + (i % 30),
                MainActors = new List<string> { $"actor-{i}a", $"actor-{i}b" },
            });
        }

        ctx.SaveChanges();
        return ids;
    }

    private static List<Guid> SeedAlbums(DbContextOptions<AppDbContext> options, int count)
    {
        var ids = new List<Guid>(count);
        using var ctx = new SqliteCatalogDbContext(options);
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            ctx.Albums.Add(new Album
            {
                Id = id,
                Title = $"album-{i}-{id:N}".Substring(0, Math.Min(30, $"album-{i}-{id:N}".Length)),
                Band = i % 2 == 0 ? $"band-{i}" : null,
                ReleaseYear = 1990 + (i % 30),
                Genre = i % 3 == 0 ? $"genre-{i}" : null,
            });
        }

        ctx.SaveChanges();
        return ids;
    }
}

/// <summary>
/// Test-only <see cref="AppDbContext"/> that adds a JSON value converter for
/// <see cref="Movie.MainActors"/> so the PostgreSQL <c>text[]</c> mapping can be stored
/// on SQLite. All production configurations still apply via the base
/// <see cref="AppDbContext.OnModelCreating"/>; this only overrides the one mapping SQLite
/// cannot represent natively (mirrors the Security/Persistence test harnesses).
/// </summary>
internal sealed class SqliteCatalogDbContext : AppDbContext
{
    public SqliteCatalogDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var listToJson = new ValueConverter<List<string>, string>(
            v => string.Join('\u001f', v),
            v => string.IsNullOrEmpty(v)
                ? new List<string>()
                : v.Split(new[] { '\u001f' }, StringSplitOptions.None).ToList());

        var listComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
            v => v == null ? 0 : v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode())),
            v => v.ToList());

        modelBuilder.Entity<Movie>()
            .Property(m => m.MainActors)
            .HasConversion(listToJson, listComparer);
    }
}
