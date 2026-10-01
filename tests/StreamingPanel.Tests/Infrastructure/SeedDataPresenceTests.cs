using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging.Abstractions;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Enums;
using StreamingPanel.Infrastructure.Persistence;
using StreamingPanel.Infrastructure.Persistence.Seed;
using StreamingPanel.Infrastructure.Security;

namespace StreamingPanel.Tests.Infrastructure;

/// <summary>
/// Smoke tests confirming the migration + seed produces the expected initial data
/// set with cover images (R11.1–R11.4).
///
/// Feature: streaming-panel, task 7.9 (infrastructure smoke tests)
/// Validates: Requirements 11.1, 11.2, 11.3, 11.4
///
/// <para>
/// A SQLite in-memory database stands in for PostgreSQL: the production entity
/// configurations are applied unchanged (via <see cref="AppDbContext.OnModelCreating"/>),
/// with a single JSON value converter for the PostgreSQL <c>text[]</c>
/// <see cref="Movie.MainActors"/> mapping that SQLite cannot represent natively. The
/// real <see cref="DatabaseSeeder.SeedAsync"/> and real <see cref="PasswordHasher"/>
/// run against it, then the seeded data is asserted:
/// one Admin person whose stored password is a non-plaintext hash (R11.1, R11.2),
/// and 6 Movies and 6 Albums (within the 3–20 range) each carrying cover image bytes
/// (R11.3, R11.4).
/// </para>
/// </summary>
public class SeedDataPresenceTests
{
    private const int ExpectedMovies = 6;
    private const int ExpectedAlbums = 6;

    private static DbContextOptions<AppDbContext> Seed(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var ctx = new SeedTestDbContext(options))
        {
            ctx.Database.EnsureCreated();
        }

        using (var ctx = new SeedTestDbContext(options))
        {
            DatabaseSeeder
                .SeedAsync(ctx, new PasswordHasher(), NullLogger.Instance)
                .GetAwaiter().GetResult();
        }

        return options;
    }

    [Fact]
    public void Seed_CreatesSingleAdminPersonWithHashedPassword()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = Seed(connection);

        using var verify = new SeedTestDbContext(options);

        // Exactly one Admin person exists (R11.1).
        var admins = verify.Persons.Where(p => p.Role == Role.Admin).ToList();
        Assert.Single(admins);

        var admin = admins[0];
        Assert.Equal(DatabaseSeeder.AdminEmail, admin.Email);

        // The password is stored as a one-way hash, never the plaintext (R11.2).
        Assert.False(string.IsNullOrWhiteSpace(admin.PasswordHash));
        Assert.NotEqual(DatabaseSeeder.AdminPassword, admin.PasswordHash);

        // The stored hash verifies against the seed password (confirms it is a real hash).
        Assert.True(new PasswordHasher().Verify(DatabaseSeeder.AdminPassword, admin.PasswordHash));
    }

    [Fact]
    public void Seed_CreatesMoviesWithinRangeEachWithCover()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = Seed(connection);

        using var verify = new SeedTestDbContext(options);
        var movies = verify.Movies.ToList();

        // Between 3 and 20 movies; the seeder ships exactly 6 (R11.3).
        Assert.Equal(ExpectedMovies, movies.Count);
        Assert.InRange(movies.Count, 3, 20);

        // Every movie carries cover image bytes and a content type (R11.3).
        Assert.All(movies, m =>
        {
            Assert.NotNull(m.CoverImageData);
            Assert.NotEmpty(m.CoverImageData!);
            Assert.False(string.IsNullOrWhiteSpace(m.ContentType));
        });
    }

    [Fact]
    public void Seed_CreatesAlbumsWithinRangeEachWithCover()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = Seed(connection);

        using var verify = new SeedTestDbContext(options);
        var albums = verify.Albums.ToList();

        // Between 3 and 20 albums; the seeder ships exactly 6 (R11.4).
        Assert.Equal(ExpectedAlbums, albums.Count);
        Assert.InRange(albums.Count, 3, 20);

        // Every album carries cover image bytes and a content type (R11.4).
        Assert.All(albums, a =>
        {
            Assert.NotNull(a.CoverImageData);
            Assert.NotEmpty(a.CoverImageData!);
            Assert.False(string.IsNullOrWhiteSpace(a.ContentType));
        });
    }
}

/// <summary>
/// Test-only <see cref="AppDbContext"/> that adds a JSON value converter for
/// <see cref="Movie.MainActors"/> so the PostgreSQL <c>text[]</c> mapping can be
/// stored on SQLite. All production configurations still apply via the base
/// <see cref="AppDbContext.OnModelCreating"/>.
/// </summary>
internal sealed class SeedTestDbContext : AppDbContext
{
    public SeedTestDbContext(DbContextOptions<AppDbContext> options)
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
