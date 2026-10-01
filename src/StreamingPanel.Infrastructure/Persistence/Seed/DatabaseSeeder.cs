using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Enums;
using StreamingPanel.Core.Interfaces;

namespace StreamingPanel.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds the initial data set after migrations run. Idempotent: each record is matched by
/// a stable natural key (admin email, movie/album titles) and inserted only when absent, so
/// re-running never duplicates. Every insert shares one transaction that rolls back on any
/// failure, so a failed seed leaves nothing partial behind.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>Stable natural key for the default administrator account.</summary>
    public const string AdminEmail = "admin@streamingpanel.local";

    /// <summary>Seed password; only its hash is persisted.</summary>
    public const string AdminPassword = "Admin123!";

    private const string AdminName = "Default Administrator";

    /// <summary>
    /// Inserts the default admin, sample movies, and sample albums that aren't already
    /// present. On failure the transaction rolls back and the error is logged and rethrown
    /// so startup aborts rather than continuing on partially seeded data.
    /// </summary>
    public static async Task SeedAsync(
        AppDbContext dbContext,
        IPasswordHasher passwordHasher,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(logger);

        // Figure out what's missing before opening a transaction, so an already-seeded
        // database just runs the cheap existence checks and writes nothing.
        var newPeople = await BuildMissingPeopleAsync(dbContext, passwordHasher, cancellationToken);
        var newMovies = await BuildMissingMoviesAsync(dbContext, cancellationToken);
        var newAlbums = await BuildMissingAlbumsAsync(dbContext, cancellationToken);

        if (newPeople.Count == 0 && newMovies.Count == 0 && newAlbums.Count == 0)
        {
            logger.LogInformation("Seed data already present; nothing to insert.");
            return;
        }

        // One transaction for every insert: all seed rows commit together or none do.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (newPeople.Count > 0)
            {
                await dbContext.Persons.AddRangeAsync(newPeople, cancellationToken);
            }

            if (newMovies.Count > 0)
            {
                await dbContext.Movies.AddRangeAsync(newMovies, cancellationToken);
            }

            if (newAlbums.Count > 0)
            {
                await dbContext.Albums.AddRangeAsync(newAlbums, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Seed complete: inserted {People} person(s), {Movies} movie(s), {Albums} album(s).",
                newPeople.Count, newMovies.Count, newAlbums.Count);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);

            // Detach the entities that failed to persist so a caller reusing the context
            // doesn't inherit a dirty change tracker.
            foreach (var entry in dbContext.ChangeTracker.Entries().ToList())
            {
                entry.State = EntityState.Detached;
            }

            logger.LogError(ex, "Database seeding failed; the seed transaction was rolled back.");
            throw new InvalidOperationException(
                "Database seeding failed; the seed transaction was rolled back so no partial seed data remains.",
                ex);
        }
    }

    private static async Task<List<Person>> BuildMissingPeopleAsync(
        AppDbContext dbContext,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        var people = new List<Person>();

        // Matched by the admin email.
        var adminExists = await dbContext.Persons
            .AsNoTracking()
            .AnyAsync(p => p.Email == AdminEmail, cancellationToken);

        if (!adminExists)
        {
            people.Add(new Person
            {
                Id = Guid.NewGuid(),
                Name = AdminName,
                Email = AdminEmail,
                PasswordHash = passwordHasher.Hash(AdminPassword), // only the hash is stored
                Role = Role.Admin,
                CreatedAt = DateTime.UtcNow,
            });
        }

        return people;
    }

    private static async Task<List<Movie>> BuildMissingMoviesAsync(
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var samples = SampleMovies();
        var titles = samples.Select(m => m.Title).ToList();

        // Matched by title.
        var existingTitles = await dbContext.Movies
            .AsNoTracking()
            .Where(m => titles.Contains(m.Title))
            .Select(m => m.Title)
            .ToListAsync(cancellationToken);

        var existing = existingTitles.ToHashSet();
        return samples.Where(m => !existing.Contains(m.Title)).ToList();
    }

    private static async Task<List<Album>> BuildMissingAlbumsAsync(
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var samples = SampleAlbums();
        var titles = samples.Select(a => a.Title).ToList();

        // Matched by title.
        var existingTitles = await dbContext.Albums
            .AsNoTracking()
            .Where(a => titles.Contains(a.Title))
            .Select(a => a.Title)
            .ToListAsync(cancellationToken);

        var existing = existingTitles.ToHashSet();
        return samples.Where(a => !existing.Contains(a.Title)).ToList();
    }

    /// <summary>Sample movies, each with a small valid PNG cover. Title is the seed key.</summary>
    private static List<Movie> SampleMovies() =>
    [
        NewMovie("The Silent Horizon", "Aurora Studios", 2015, [59, 130, 246],
            ["Lena Marsh", "Devon Clarke"]),
        NewMovie("Clockwork Rivers", "Meridian Pictures", 1998, [16, 185, 129],
            ["Ravi Anand", "Sofia Keller", "Marcus Lin"]),
        NewMovie("Echoes of Tomorrow", "Nova Film Group", 2021, [239, 68, 68],
            ["Priya Nair"]),
        NewMovie("Dust and Starlight", "Harbor Lane", 1972, [234, 179, 8],
            ["Tom Byrne", "Aiko Mori", "Grace Oduya", "Felix Vance"]),
        NewMovie("The Last Cartographer", "Summit Reel", 2009, [168, 85, 247],
            ["Omar Said", "Hannah Webb"]),
        NewMovie("Northern Signals", "Glacier Films", 2018, [20, 184, 166],
            []),
    ];

    /// <summary>Sample albums, each with a small valid PNG cover. Title is the seed key.</summary>
    private static List<Album> SampleAlbums() =>
    [
        NewAlbum("Midnight Frequencies", "The Hollow Keys", 2016, "Synthwave", [99, 102, 241]),
        NewAlbum("Paper Lanterns", "Riverbend", 2004, "Folk", [34, 197, 94]),
        NewAlbum("Concrete Gardens", "Ash & Ember", 2020, "Indie Rock", [249, 115, 22]),
        NewAlbum("Tidal Memory", "Blue Meridian", 1989, "Jazz", [14, 165, 233]),
        NewAlbum("Gravity Wells", "Orbital Theory", 2022, "Electronic", [217, 70, 239]),
        NewAlbum("Quiet Thunder", "Marrow Lane", 1995, "Blues", [245, 158, 11]),
    ];

    private static Movie NewMovie(
        string title, string studio, int releaseYear, byte[] rgb, List<string> actors) =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Studio = studio,
            ReleaseYear = releaseYear,
            MainActors = actors,
            CoverImageData = SampleCovers.SolidColor(rgb[0], rgb[1], rgb[2]),
            ContentType = SampleCovers.ContentType,
        };

    private static Album NewAlbum(
        string title, string band, int releaseYear, string genre, byte[] rgb) =>
        new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Band = band,
            ReleaseYear = releaseYear,
            Genre = genre,
            CoverImageData = SampleCovers.SolidColor(rgb[0], rgb[1], rgb[2]),
            ContentType = SampleCovers.ContentType,
        };
}
