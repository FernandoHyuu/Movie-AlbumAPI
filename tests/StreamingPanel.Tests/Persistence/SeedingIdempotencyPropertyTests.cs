using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging.Abstractions;
using StreamingPanel.Core.Entities;
using StreamingPanel.Infrastructure.Persistence;
using StreamingPanel.Infrastructure.Persistence.Seed;
using StreamingPanel.Infrastructure.Security;

namespace StreamingPanel.Tests.Persistence;

/// <summary>
/// Property-based tests for <see cref="DatabaseSeeder"/>.
///
/// Feature: streaming-panel, Property 26: Seeding is idempotent
///
/// For any number of repeated seed runs, the resulting seeded data set is identical
/// to the set produced by a single run (same records, no duplicates).
///
/// Validates: Requirements 11.5
///
/// Database provider: Microsoft.EntityFrameworkCore.Sqlite (in-memory). SQLite is
/// used instead of the EF Core InMemory provider because <see cref="DatabaseSeeder"/>
/// opens a real transaction via <c>BeginTransactionAsync</c>, which the InMemory
/// provider does not support. The production <see cref="MovieConfiguration"/> maps
/// <c>MainActors</c> to a PostgreSQL <c>text[]</c>, which SQLite cannot store
/// natively; <see cref="SqliteAppDbContext"/> supplies a JSON value converter for
/// that one property so the real seeder, entities, and configurations are exercised
/// unchanged against SQLite.
/// </summary>
public class SeedingIdempotencyPropertyTests
{
    // The seeder's fixed seed set: 1 admin Person, 6 Movies, 6 Albums.
    private const int ExpectedPersons = 1;
    private const int ExpectedMovies = 6;
    private const int ExpectedAlbums = 6;

    /// <summary>
    /// Running <see cref="DatabaseSeeder.SeedAsync"/> N times (N in 1..5) leaves the
    /// database with exactly the single-run seed set: no duplicate persons, movies,
    /// or albums, regardless of how many times the seed is applied.
    ///
    /// Feature: streaming-panel, Property 26: Seeding is idempotent
    /// Validates: Requirements 11.5
    /// </summary>
    [Fact]
    public void SeedingIsIdempotentAcrossRepeatedRuns()
    {
        Gen.Int[1, 5].Sample(
            runCount =>
            {
                // A fresh, isolated in-memory database per sample. The SQLite
                // in-memory database lives as long as its connection is open.
                using var connection = new SqliteConnection("DataSource=:memory:");
                connection.Open();

                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(connection)
                    .Options;

                var hasher = new PasswordHasher();

                using (var ctx = new SqliteAppDbContext(options))
                {
                    ctx.Database.EnsureCreated();
                }

                // Run the real seeder runCount times against the same database.
                for (var i = 0; i < runCount; i++)
                {
                    using var ctx = new SqliteAppDbContext(options);
                    DatabaseSeeder.SeedAsync(ctx, hasher, NullLogger.Instance).GetAwaiter().GetResult();
                }

                using var verify = new SqliteAppDbContext(options);
                var persons = verify.Persons.Count();
                var movies = verify.Movies.Count();
                var albums = verify.Albums.Count();

                // Also assert the natural keys are unique (no duplicate rows that
                // happen to sum to the same count by coincidence).
                var distinctEmails = verify.Persons.Select(p => p.Email).Distinct().Count();
                var distinctMovieTitles = verify.Movies.Select(m => m.Title).Distinct().Count();
                var distinctAlbumTitles = verify.Albums.Select(a => a.Title).Distinct().Count();

                return persons == ExpectedPersons
                    && movies == ExpectedMovies
                    && albums == ExpectedAlbums
                    && distinctEmails == ExpectedPersons
                    && distinctMovieTitles == ExpectedMovies
                    && distinctAlbumTitles == ExpectedAlbums;
            },
            iter: 100);
    }
}

/// <summary>
/// Test-only <see cref="AppDbContext"/> that adds a JSON value converter for
/// <see cref="Movie.MainActors"/> so the PostgreSQL <c>text[]</c> mapping can be
/// stored on SQLite. All production configurations are still applied by the base
/// <see cref="AppDbContext.OnModelCreating"/>; this only overrides the one mapping
/// SQLite cannot represent natively.
/// </summary>
internal sealed class SqliteAppDbContext : AppDbContext
{
    public SqliteAppDbContext(DbContextOptions<AppDbContext> options)
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
