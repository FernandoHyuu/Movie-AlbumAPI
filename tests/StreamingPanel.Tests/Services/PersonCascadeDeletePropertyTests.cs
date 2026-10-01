using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Enums;
using StreamingPanel.Infrastructure.Persistence;
using StreamingPanel.Infrastructure.Repositories;
using StreamingPanel.Infrastructure.Security;
using StreamingPanel.Infrastructure.Services;

namespace StreamingPanel.Tests.Services;

/// <summary>
/// Property-based test for <see cref="PersonService.DeleteAsync"/> cascade delete.
///
/// Feature: streaming-panel, Property 13: Deleting a Person removes all its children
///
/// For a Person created with a random number (0..10) of addresses and (0..10) phones,
/// after <see cref="PersonService.DeleteAsync"/> succeeds there are zero Address rows and
/// zero Phone rows referencing that PersonId, and the Person row itself is gone.
///
/// Validates: Requirements 5.5
///
/// Database provider: Microsoft.EntityFrameworkCore.Sqlite (in-memory). SQLite is used
/// instead of the EF Core InMemory provider so the real <see cref="AppDbContext"/>,
/// entity configurations, <see cref="PersonService"/> (which opens a real transaction via
/// <c>BeginTransactionAsync</c>), <see cref="PersonRepository"/>, and
/// <see cref="PasswordHasher"/> are exercised unchanged against a relational store.
///
/// SQLite cascade delete: EF Core's SQLite provider enables <c>PRAGMA foreign_keys=ON</c>
/// per connection by default, and <c>EnsureCreated</c> emits the Address/Phone foreign
/// keys with <c>ON DELETE CASCADE</c> derived from the model's
/// <see cref="DeleteBehavior.Cascade"/> (see <c>PersonConfiguration</c>). EF Core also
/// tracks the loaded children (the repository eager-loads them) and issues matching DELETE
/// statements, so the cascade is enforced both at the model and database level. The test
/// asserts the children are actually gone, which proves the cascade fired regardless of
/// mechanism. Production uses PostgreSQL, whose cascade is configured by the same
/// <see cref="DeleteBehavior.Cascade"/> mapping.
///
/// The production <see cref="Movie"/> mapping stores <c>MainActors</c> as a PostgreSQL
/// <c>text[]</c>, which SQLite cannot represent natively; the test-only
/// <see cref="SqlitePersonDbContext"/> supplies a JSON value converter for that one
/// property (none of the Person/Address/Phone mappings exercised here are affected).
/// Each sample runs against a fresh in-memory database for isolation; the property
/// executes a minimum of 100 iterations.
/// </summary>
public class PersonCascadeDeletePropertyTests
{
    private static readonly string[] ValidRoles =
    {
        nameof(Role.Admin),
        nameof(Role.User_Movie),
        nameof(Role.User_Album),
        nameof(Role.User_Full),
    };

    private static readonly Gen<string> GenEmail =
        from local in Gen.Char['a', 'z'].Array[1, 20]
        from domain in Gen.Char['a', 'z'].Array[1, 15]
        from tld in Gen.OneOfConst("com", "org", "net", "io", "dev")
        select $"{new string(local)}@{new string(domain)}.{tld}";

    private static readonly Gen<string> GenPassword =
        Gen.Char[' ', '~'].Array[8, 40].Select(cs => new string(cs));

    private static readonly Gen<string> GenRole = Gen.OneOfConst(ValidRoles);

    private static readonly Gen<AddressDto> GenAddress =
        from street in Gen.String[Gen.Char.AlphaNumeric, 1, 20]
        from city in Gen.String[Gen.Char.AlphaNumeric, 1, 20]
        from state in Gen.String[Gen.Char.AlphaNumeric, 1, 20]
        from zip in Gen.String[Gen.Char.AlphaNumeric, 1, 10]
        select new AddressDto(street, city, state, zip);

    private static readonly Gen<PhoneDto> GenPhone =
        from number in Gen.String[Gen.Char.AlphaNumeric, 1, 20]
        from type in Gen.OneOfConst("Mobile", "Home", "Work")
        select new PhoneDto(number, type);

    /// <summary>
    /// Property 13 — Deleting a Person removes all its children.
    ///
    /// Create a Person with 0..10 addresses and 0..10 phones via
    /// <see cref="PersonService.CreateAsync"/>, confirm the children were persisted, then
    /// <see cref="PersonService.DeleteAsync"/> the Person. After a successful delete the
    /// Person row is gone and NO Address or Phone row references that PersonId.
    ///
    /// Feature: streaming-panel, Property 13: Deleting a Person removes all its children
    /// Validates: Requirements 5.5
    /// </summary>
    [Fact]
    public void DeletingAPersonRemovesAllItsChildren()
    {
        (from email in GenEmail
         from password in GenPassword
         from role in GenRole
         from addresses in GenAddress.List[0, 10]
         from phones in GenPhone.List[0, 10]
         select (email, password, role, addresses, phones))
            .Sample(
                input =>
                {
                    var (email, password, role, addresses, phones) = input;

                    using var connection = OpenDatabase();
                    var options = BuildOptions(connection);

                    // Create the Person (with its children) through the real service.
                    Guid personId;
                    using (var ctx = new SqlitePersonDbContext(options))
                    {
                        var service = BuildService(ctx);
                        var dto = new PersonWriteDto(
                            Name: "Test Person",
                            Email: email,
                            Role: role,
                            Password: password,
                            Addresses: addresses,
                            Phones: phones);

                        var created = service.CreateAsync(dto).GetAwaiter().GetResult();
                        if (!created.IsSuccess)
                        {
                            return false;
                        }

                        personId = created.Value.Id;
                    }

                    // Confirm the children were actually persisted so the delete has
                    // something to cascade (and 0-child cases are still valid).
                    using (var verify = new SqlitePersonDbContext(options))
                    {
                        var addressesBefore = verify.Set<Address>().Count(a => a.PersonId == personId);
                        var phonesBefore = verify.Set<Phone>().Count(p => p.PersonId == personId);
                        if (addressesBefore != addresses.Count || phonesBefore != phones.Count)
                        {
                            return false;
                        }
                    }

                    // Delete the Person through the real service.
                    using (var ctx = new SqlitePersonDbContext(options))
                    {
                        var service = BuildService(ctx);
                        var deleted = service.DeleteAsync(personId).GetAwaiter().GetResult();
                        if (!deleted.IsSuccess)
                        {
                            return false;
                        }
                    }

                    // After the delete: the Person row is gone and NO Address or Phone row
                    // references that PersonId (cascade removed every child).
                    using var check = new SqlitePersonDbContext(options);
                    var personGone = !check.Persons.Any(p => p.Id == personId);
                    var noAddresses = !check.Set<Address>().Any(a => a.PersonId == personId);
                    var noPhones = !check.Set<Phone>().Any(p => p.PersonId == personId);

                    return personGone && noAddresses && noPhones;
                },
                iter: 100);
    }

    // ---- Shared test harness -------------------------------------------------

    private static SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = BuildOptions(connection);
        using var ctx = new SqlitePersonDbContext(options);
        ctx.Database.EnsureCreated();

        return connection;
    }

    private static DbContextOptions<AppDbContext> BuildOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

    private static PersonService BuildService(AppDbContext ctx)
    {
        var repository = new PersonRepository(ctx);
        var hasher = new PasswordHasher();
        return new PersonService(repository, ctx, hasher);
    }
}

/// <summary>
/// Test-only <see cref="AppDbContext"/> that adds a JSON value converter for
/// <see cref="Movie.MainActors"/> so the PostgreSQL <c>text[]</c> mapping can be
/// stored on SQLite. All production configurations still apply via the base
/// <see cref="AppDbContext.OnModelCreating"/>; this only overrides the one mapping
/// SQLite cannot represent natively (mirrors the other test harnesses).
/// </summary>
internal sealed class SqlitePersonDbContext : AppDbContext
{
    public SqlitePersonDbContext(DbContextOptions<AppDbContext> options)
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
