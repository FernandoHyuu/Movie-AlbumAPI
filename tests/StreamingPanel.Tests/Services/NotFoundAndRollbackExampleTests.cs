using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Enums;
using StreamingPanel.Core.Interfaces;
using StreamingPanel.Infrastructure.Persistence;
using StreamingPanel.Infrastructure.Persistence.Seed;
using StreamingPanel.Infrastructure.Repositories;
using StreamingPanel.Infrastructure.Security;
using StreamingPanel.Infrastructure.Services;

namespace StreamingPanel.Tests.Services;

/// <summary>
/// Example (by-example) xUnit tests for the service-layer not-found and transactional
/// rollback paths that complement the property-based coverage.
///
/// Feature: streaming-panel, Task 7.7.
///
/// Two families of scenarios are pinned here, each as an isolated [Fact]:
///
/// 1. Not-found → 404 across every aggregate (R5.9, R6.6, R7.7) and the two distinct
///    cover 404 reasons (R8.5 missing image on an existing record; R8.7 missing record).
///    These run the REAL <see cref="PersonService"/>/<see cref="MovieService"/>/
///    <see cref="AlbumService"/> over the real repositories backed by an empty (or
///    minimally seeded) SQLite in-memory database, mirroring the harness used by
///    <see cref="PersonCascadeDeletePropertyTests"/> and <see cref="SeedingIdempotencyPropertyTests"/>.
///
/// 2. Rollback on injected failure. <see cref="PersonService.DeleteAsync"/> wraps the
///    delete in a transaction and must return <see cref="ErrorCode.Internal"/> (500),
///    rolling back, when the delete fails for a non-database reason (R5.6).
///    <see cref="DatabaseSeeder.SeedAsync"/> wraps its inserts in a transaction and must
///    roll back, leaving no partial data, when an insert step fails (R11.6). Both inject
///    a <b>Moq</b> double (an <see cref="IPersonRepository"/> whose
///    <c>SaveChangesAsync</c> throws for the delete case; an <see cref="IPasswordHasher"/>
///    whose <c>Hash</c> throws for the seed case) to force the failure deterministically.
///
/// Database provider: Microsoft.EntityFrameworkCore.Sqlite (in-memory). SQLite is used
/// (not the EF InMemory provider) so the real transaction opened via
/// <c>BeginTransactionAsync</c> inside <see cref="PersonService"/> and
/// <see cref="DatabaseSeeder"/> executes against a relational store. The production
/// <see cref="Movie"/> mapping stores <c>MainActors</c> as a PostgreSQL <c>text[]</c>,
/// which SQLite cannot represent natively; <see cref="SqliteNotFoundDbContext"/> supplies
/// a JSON value converter for that one property so every other production configuration
/// runs unchanged.
///
/// Validates: Requirements 5.6, 5.9, 6.6, 7.7, 8.5, 8.7, 11.6
/// </summary>
public class NotFoundAndRollbackExampleTests
{
    // ---- Not-found → 404: Person (R5.9) --------------------------------------

    /// <summary>
    /// R5.9 — Reading a Person by an id that does not exist returns a 404 NotFound
    /// failure and exposes no value.
    /// </summary>
    [Fact]
    public void PersonGetByIdAsync_WithUnknownId_ReturnsNotFound()
    {
        using var connection = OpenDatabase();
        using var ctx = new SqliteNotFoundDbContext(BuildOptions(connection));
        var service = BuildPersonService(ctx);

        var result = service.GetByIdAsync(Guid.NewGuid()).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    /// <summary>
    /// R5.9 — Updating a Person by an unknown id returns a 404 NotFound failure. The
    /// payload is otherwise valid so the only reason for failure is the missing record.
    /// </summary>
    [Fact]
    public void PersonUpdateAsync_WithUnknownId_ReturnsNotFound()
    {
        using var connection = OpenDatabase();
        using var ctx = new SqliteNotFoundDbContext(BuildOptions(connection));
        var service = BuildPersonService(ctx);

        var dto = new PersonWriteDto(
            Name: "Nobody",
            Email: "nobody@example.com",
            Role: nameof(Role.Admin),
            Password: "password123",
            Addresses: Array.Empty<AddressDto>(),
            Phones: Array.Empty<PhoneDto>());

        var result = service.UpdateAsync(Guid.NewGuid(), dto).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    /// <summary>
    /// R5.9 — Deleting a Person by an unknown id returns a 404 NotFound failure (the
    /// delete short-circuits before opening a transaction).
    /// </summary>
    [Fact]
    public void PersonDeleteAsync_WithUnknownId_ReturnsNotFound()
    {
        using var connection = OpenDatabase();
        using var ctx = new SqliteNotFoundDbContext(BuildOptions(connection));
        var service = BuildPersonService(ctx);

        var result = service.DeleteAsync(Guid.NewGuid()).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    // ---- Not-found → 404: Movie (R6.6, R8.5, R8.7) ---------------------------

    /// <summary>R6.6 — Reading a Movie by an unknown id returns 404 NotFound.</summary>
    [Fact]
    public void MovieGetByIdAsync_WithUnknownId_ReturnsNotFound()
    {
        using var connection = OpenDatabase();
        using var ctx = new SqliteNotFoundDbContext(BuildOptions(connection));
        var service = new MovieService(new MovieRepository(ctx));

        var result = service.GetByIdAsync(Guid.NewGuid()).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    /// <summary>R6.6 — Updating a Movie by an unknown id returns 404 NotFound.</summary>
    [Fact]
    public void MovieUpdateAsync_WithUnknownId_ReturnsNotFound()
    {
        using var connection = OpenDatabase();
        using var ctx = new SqliteNotFoundDbContext(BuildOptions(connection));
        var service = new MovieService(new MovieRepository(ctx));

        var dto = new MovieWriteDto("A Title", "A Studio", 2000, new List<string>());
        var result = service.UpdateAsync(Guid.NewGuid(), dto).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    /// <summary>R6.6 — Deleting a Movie by an unknown id returns 404 NotFound.</summary>
    [Fact]
    public void MovieDeleteAsync_WithUnknownId_ReturnsNotFound()
    {
        using var connection = OpenDatabase();
        using var ctx = new SqliteNotFoundDbContext(BuildOptions(connection));
        var service = new MovieService(new MovieRepository(ctx));

        var result = service.DeleteAsync(Guid.NewGuid()).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    /// <summary>
    /// R8.7 — Requesting a Movie cover for an id that does not exist returns 404 NotFound
    /// (the record itself is missing).
    /// </summary>
    [Fact]
    public void MovieGetCoverAsync_WithUnknownId_ReturnsNotFound()
    {
        using var connection = OpenDatabase();
        using var ctx = new SqliteNotFoundDbContext(BuildOptions(connection));
        var service = new MovieService(new MovieRepository(ctx));

        var result = service.GetCoverAsync(Guid.NewGuid()).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    /// <summary>
    /// R8.5 — Requesting a Movie cover for an EXISTING record that has NO stored image
    /// returns 404 NotFound. We insert a movie with null cover bytes directly, then fetch
    /// its cover; the service distinguishes this from a missing record but both map to 404.
    /// </summary>
    [Fact]
    public void MovieGetCoverAsync_WhenRecordHasNoStoredImage_ReturnsNotFound()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        var movieId = Guid.NewGuid();
        using (var seed = new SqliteNotFoundDbContext(options))
        {
            seed.Movies.Add(new Movie
            {
                Id = movieId,
                Title = "No Cover Movie",
                Studio = "Studio",
                ReleaseYear = 2001,
                MainActors = new List<string>(),
                CoverImageData = null,
                ContentType = null,
            });
            seed.SaveChanges();
        }

        using var ctx = new SqliteNotFoundDbContext(options);
        var service = new MovieService(new MovieRepository(ctx));

        var result = service.GetCoverAsync(movieId).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    // ---- Not-found → 404: Album (R7.7, R8.5, R8.7) ---------------------------

    /// <summary>R7.7 — Reading an Album by an unknown id returns 404 NotFound.</summary>
    [Fact]
    public void AlbumGetByIdAsync_WithUnknownId_ReturnsNotFound()
    {
        using var connection = OpenDatabase();
        using var ctx = new SqliteNotFoundDbContext(BuildOptions(connection));
        var service = new AlbumService(new AlbumRepository(ctx));

        var result = service.GetByIdAsync(Guid.NewGuid()).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    /// <summary>R7.7 — Updating an Album by an unknown id returns 404 NotFound.</summary>
    [Fact]
    public void AlbumUpdateAsync_WithUnknownId_ReturnsNotFound()
    {
        using var connection = OpenDatabase();
        using var ctx = new SqliteNotFoundDbContext(BuildOptions(connection));
        var service = new AlbumService(new AlbumRepository(ctx));

        var dto = new AlbumWriteDto("An Album", "A Band", 2000, "Rock");
        var result = service.UpdateAsync(Guid.NewGuid(), dto).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    /// <summary>R7.7 — Deleting an Album by an unknown id returns 404 NotFound.</summary>
    [Fact]
    public void AlbumDeleteAsync_WithUnknownId_ReturnsNotFound()
    {
        using var connection = OpenDatabase();
        using var ctx = new SqliteNotFoundDbContext(BuildOptions(connection));
        var service = new AlbumService(new AlbumRepository(ctx));

        var result = service.DeleteAsync(Guid.NewGuid()).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    /// <summary>
    /// R8.7 — Requesting an Album cover for an id that does not exist returns 404 NotFound
    /// (the record itself is missing).
    /// </summary>
    [Fact]
    public void AlbumGetCoverAsync_WithUnknownId_ReturnsNotFound()
    {
        using var connection = OpenDatabase();
        using var ctx = new SqliteNotFoundDbContext(BuildOptions(connection));
        var service = new AlbumService(new AlbumRepository(ctx));

        var result = service.GetCoverAsync(Guid.NewGuid()).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    /// <summary>
    /// R8.5 — Requesting an Album cover for an EXISTING record that has NO stored image
    /// returns 404 NotFound.
    /// </summary>
    [Fact]
    public void AlbumGetCoverAsync_WhenRecordHasNoStoredImage_ReturnsNotFound()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        var albumId = Guid.NewGuid();
        using (var seed = new SqliteNotFoundDbContext(options))
        {
            seed.Albums.Add(new Album
            {
                Id = albumId,
                Title = "No Cover Album",
                Band = "Band",
                ReleaseYear = 2003,
                Genre = "Jazz",
                CoverImageData = null,
                ContentType = null,
            });
            seed.SaveChanges();
        }

        using var ctx = new SqliteNotFoundDbContext(options);
        var service = new AlbumService(new AlbumRepository(ctx));

        var result = service.GetCoverAsync(albumId).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    // ---- Rollback: Person delete on injected failure (R5.6) ------------------

    /// <summary>
    /// R5.6 — When deleting a Person fails for a non-database reason, the service rolls
    /// back the transaction and returns <see cref="ErrorCode.Internal"/> (500), retaining
    /// the Person and its children.
    ///
    /// A Moq <see cref="IPersonRepository"/> double returns a real Person for
    /// <c>GetEntityAsync</c> (so the delete proceeds past the 404 guard) but throws an
    /// <see cref="InvalidOperationException"/> from <c>SaveChangesAsync</c>. This is NOT a
    /// database-availability shape (not Npgsql/Timeout), so the service's catch filter
    /// treats it as an internal failure, rolls back, and reports 500 rather than letting
    /// it escape as a 503. A real SQLite <see cref="AppDbContext"/> is supplied alongside
    /// the mock so the service's <c>BeginTransactionAsync</c>/<c>RollbackAsync</c> run for
    /// real; the previously-seeded Person remains present after the failed delete.
    /// </summary>
    [Fact]
    public void PersonDeleteAsync_WhenSaveThrowsNonDatabaseError_RollsBackAndReturnsInternal()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        // Seed one real Person so a rollback has observable state to preserve.
        var person = new Person
        {
            Id = Guid.NewGuid(),
            Name = "Retained Person",
            Email = "retained@example.com",
            PasswordHash = new PasswordHasher().Hash("password123"),
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow,
        };

        using (var seed = new SqliteNotFoundDbContext(options))
        {
            seed.Persons.Add(person);
            seed.SaveChanges();
        }

        using var ctx = new SqliteNotFoundDbContext(options);

        // Moq repository double: resolves the entity, but its persist step throws a
        // non-database exception to trigger the service's rollback path (R5.6).
        var repository = new Mock<IPersonRepository>(MockBehavior.Strict);
        repository.Setup(r => r.GetEntityAsync(person.Id)).ReturnsAsync(person);
        repository.Setup(r => r.Remove(It.IsAny<Person>()));
        repository.Setup(r => r.SaveChangesAsync())
            .ThrowsAsync(new InvalidOperationException("Simulated non-database failure during delete."));

        var service = new PersonService(repository.Object, ctx, new PasswordHasher());

        var result = service.DeleteAsync(person.Id).GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.Internal, result.ErrorCode);

        // The Person is retained after the rolled-back delete (R5.6).
        using var verify = new SqliteNotFoundDbContext(options);
        Assert.True(verify.Persons.Any(p => p.Id == person.Id));
    }

    // ---- Rollback: seed on injected failure (R11.6) --------------------------

    /// <summary>
    /// R11.6 — When the seed fails, <see cref="DatabaseSeeder.SeedAsync"/> commits no
    /// partial seed data and surfaces the failure as an <see cref="InvalidOperationException"/>.
    ///
    /// A Moq <see cref="IPasswordHasher"/> whose <c>Hash</c> throws forces the admin-Person
    /// build to fail. The hasher is invoked while assembling the missing rows, so the
    /// failure aborts the seed and nothing is committed. Awaiting a faulted Task via
    /// <c>GetAwaiter().GetResult()</c> rethrows the original exception unwrapped, so we
    /// assert on <see cref="InvalidOperationException"/> directly. A real SQLite context
    /// backs the seeder so the resulting row counts are observable: all three catalogs
    /// remain empty (R11.6).
    /// </summary>
    [Fact]
    public void SeedAsync_WhenHasherThrows_PropagatesAndCommitsNoData()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        var hasher = new Mock<IPasswordHasher>(MockBehavior.Strict);
        hasher.Setup(h => h.Hash(It.IsAny<string>()))
            .Throws(new InvalidOperationException("Simulated hashing failure during seed."));

        using var ctx = new SqliteNotFoundDbContext(options);

        Assert.Throws<InvalidOperationException>(
            () => DatabaseSeeder.SeedAsync(ctx, hasher.Object, NullLogger.Instance).GetAwaiter().GetResult());

        // No partial seed data was committed (R11.6): the catalogs remain empty.
        using var verify = new SqliteNotFoundDbContext(options);
        Assert.Equal(0, verify.Persons.Count());
        Assert.Equal(0, verify.Movies.Count());
        Assert.Equal(0, verify.Albums.Count());
    }

    /// <summary>
    /// R11.6 (companion to the rollback test) — A seeder run against a healthy context
    /// commits every seed row (1 admin Person, 6 Movies, 6 Albums). Contrasting this with
    /// <see cref="SeedAsync_WhenHasherThrows_PropagatesAndCommitsNoData"/> shows the seed is
    /// all-or-nothing: on failure none of these rows persist, confirming the rollback keeps
    /// the database free of partial seed data.
    /// </summary>
    [Fact]
    public void SeedAsync_WithHealthyContext_CommitsAllRows()
    {
        using var connection = OpenDatabase();
        var options = BuildOptions(connection);

        using (var ctx = new SqliteNotFoundDbContext(options))
        {
            DatabaseSeeder.SeedAsync(ctx, new PasswordHasher(), NullLogger.Instance).GetAwaiter().GetResult();
        }

        using var verify = new SqliteNotFoundDbContext(options);
        Assert.Equal(1, verify.Persons.Count());
        Assert.Equal(6, verify.Movies.Count());
        Assert.Equal(6, verify.Albums.Count());
    }

    // ---- Shared test harness -------------------------------------------------

    private static SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var ctx = new SqliteNotFoundDbContext(BuildOptions(connection));
        ctx.Database.EnsureCreated();

        return connection;
    }

    private static DbContextOptions<AppDbContext> BuildOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

    private static PersonService BuildPersonService(AppDbContext ctx) =>
        new(new PersonRepository(ctx), ctx, new PasswordHasher());
}

/// <summary>
/// Test-only <see cref="AppDbContext"/> that adds a JSON value converter for
/// <see cref="Movie.MainActors"/> so the PostgreSQL <c>text[]</c> mapping can be stored
/// on SQLite. All production configurations are still applied by the base
/// <see cref="AppDbContext.OnModelCreating"/>; this only overrides the one mapping SQLite
/// cannot represent natively (mirrors the other test harnesses).
/// </summary>
internal sealed class SqliteNotFoundDbContext : AppDbContext
{
    public SqliteNotFoundDbContext(DbContextOptions<AppDbContext> options)
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
