using Microsoft.EntityFrameworkCore;
using Npgsql;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Enums;
using StreamingPanel.Core.Interfaces;
using StreamingPanel.Infrastructure.Persistence;

namespace StreamingPanel.Infrastructure.Services;

/// <summary>
/// Person aggregate business rules over <see cref="IPersonRepository"/> and
/// <see cref="AppDbContext"/>. The context is injected directly (not just the repository)
/// because update and delete replace or cascade child collections and need an explicit
/// transaction so a mid-operation failure rolls the whole thing back.
/// </summary>
public sealed class PersonService : IPersonService
{
    /// <summary>Per-page cap for the Person listing.</summary>
    public const int MaxPageSize = 50;

    /// <summary>Inclusive upper bound on a person's addresses and phones.</summary>
    public const int MaxChildren = 10;

    private readonly IPersonRepository _persons;
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;

    public PersonService(IPersonRepository persons, AppDbContext db, IPasswordHasher passwordHasher)
    {
        _persons = persons ?? throw new ArgumentNullException(nameof(persons));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<PersonDto>>> GetPagedAsync(int page, int pageSize)
    {
        // Floor the page to 1 and clamp the size into 1..MaxPageSize so a request can't
        // exceed the cap or produce a negative offset.
        var pageNumber = page < 1 ? 1 : page;
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var skip = (pageNumber - 1) * size;

        var (items, totalCount) = await _persons.GetPagedAsync(skip, size);

        var envelope = new PagedResult<PersonDto>(items, pageNumber, size, totalCount);
        return Result.Success(envelope);
    }

    /// <inheritdoc />
    public async Task<Result<PersonDto>> GetByIdAsync(Guid id)
    {
        var dto = await _persons.GetByIdAsync(id);
        return dto is null
            ? Result.Failure<PersonDto>(ErrorCode.NotFound, "The requested person was not found.")
            : Result.Success(dto);
    }

    /// <inheritdoc />
    public async Task<Result<PersonDto>> CreateAsync(PersonWriteDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // Reject anything that isn't a named role, even though validation runs earlier.
        if (!TryParseRole(dto.Role, out var role))
        {
            return Result.Failure<PersonDto>(
                ErrorCode.Validation, $"'{dto.Role}' is not a recognized role.");
        }

        // Re-check the child cardinality here so a payload that bypasses the validator
        // can't persist out-of-range collections.
        var cardinality = ValidateChildCardinality<PersonDto>(dto);
        if (cardinality is not null)
        {
            return cardinality;
        }

        // Create needs a password since the hash column is required.
        if (string.IsNullOrEmpty(dto.Password))
        {
            return Result.Failure<PersonDto>(
                ErrorCode.Validation, "A password is required to create a person.");
        }

        var person = new Person
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Email = dto.Email,
            PasswordHash = _passwordHasher.Hash(dto.Password), // only the hash is stored
            Role = role,
            CreatedAt = DateTime.UtcNow,
            Addresses = MapAddresses(dto.Addresses),
            Phones = MapPhones(dto.Phones),
        };

        await _persons.AddAsync(person);
        await _persons.SaveChangesAsync();

        return Result.Success(ToDto(person));
    }

    /// <inheritdoc />
    public async Task<Result<PersonDto>> UpdateAsync(Guid id, PersonWriteDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (!TryParseRole(dto.Role, out var role))
        {
            return Result.Failure<PersonDto>(
                ErrorCode.Validation, $"'{dto.Role}' is not a recognized role.");
        }

        var cardinality = ValidateChildCardinality<PersonDto>(dto);
        if (cardinality is not null)
        {
            return cardinality;
        }

        // Load the tracked entity with its children so the update can replace them.
        var person = await _persons.GetEntityAsync(id);
        if (person is null)
        {
            return Result.Failure<PersonDto>(ErrorCode.NotFound, "The requested person was not found.");
        }

        // Replacing the child collections is a multi-step mutation (drop the old children,
        // add the new ones), so wrap it in a transaction: a mid-operation failure must
        // leave the person and its existing children intact.
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            person.Name = dto.Name;
            person.Email = dto.Email;
            person.Role = role;

            // Re-hash only when a new password is supplied, so an update that omits it
            // keeps the existing credential.
            if (!string.IsNullOrEmpty(dto.Password))
            {
                person.PasswordHash = _passwordHasher.Hash(dto.Password);
            }

            // Clear the existing children (EF deletes the orphans) and attach the new ones.
            person.Addresses.Clear();
            person.Phones.Clear();
            foreach (var address in MapAddresses(dto.Addresses))
            {
                person.Addresses.Add(address);
            }

            foreach (var phone in MapPhones(dto.Phones))
            {
                person.Phones.Add(phone);
            }

            _persons.Update(person);
            await _persons.SaveChangesAsync();

            await transaction.CommitAsync();

            return Result.Success(ToDto(person));
        }
        catch (Exception ex) when (!IsDatabaseUnavailable(ex))
        {
            // Roll back so no partial change persists, and report 500. DB-availability
            // failures aren't caught here — they propagate for the middleware to map to 503.
            await transaction.RollbackAsync();
            return Result.Failure<PersonDto>(
                ErrorCode.Internal, "The person could not be updated.");
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id)
    {
        var person = await _persons.GetEntityAsync(id);
        if (person is null)
        {
            return Result.Failure(ErrorCode.NotFound, "The requested person was not found.");
        }

        // Deleting the Person cascades to its addresses and phones. Wrap it in a transaction
        // so a failure part-way through rolls everything back and leaves the person intact.
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            _persons.Remove(person);
            await _persons.SaveChangesAsync();

            await transaction.CommitAsync();

            return Result.Success();
        }
        catch (Exception ex) when (!IsDatabaseUnavailable(ex))
        {
            await transaction.RollbackAsync();
            return Result.Failure(
                ErrorCode.Internal, "The person could not be deleted.");
        }
    }

    /// <summary>
    /// Returns a validation failure when either child collection exceeds
    /// <see cref="MaxChildren"/>, or <c>null</c> when both are within range.
    /// </summary>
    private static Result<T>? ValidateChildCardinality<T>(PersonWriteDto dto)
    {
        if ((dto.Addresses?.Count ?? 0) > MaxChildren)
        {
            return Result.Failure<T>(
                ErrorCode.Validation, $"A person may have at most {MaxChildren} addresses.");
        }

        if ((dto.Phones?.Count ?? 0) > MaxChildren)
        {
            return Result.Failure<T>(
                ErrorCode.Validation, $"A person may have at most {MaxChildren} phones.");
        }

        return null;
    }

    /// <summary>
    /// Parses a role name, requiring an exact named match so integer-formatted strings
    /// like "0" are rejected. Mirrors the parse in <see cref="AuthService"/>.
    /// </summary>
    private static bool TryParseRole(string? value, out Role role)
    {
        role = default;
        return value is not null
            && Enum.TryParse(value, ignoreCase: false, out role)
            && Enum.IsDefined(role)
            && Enum.GetNames<Role>().Contains(value, StringComparer.Ordinal);
    }

    /// <summary>Maps the write DTO addresses to new <see cref="Address"/> children.</summary>
    private static List<Address> MapAddresses(IReadOnlyList<AddressDto>? addresses) =>
        (addresses ?? Array.Empty<AddressDto>())
            .Select(a => new Address
            {
                Id = Guid.NewGuid(),
                Street = a.Street,
                City = a.City,
                State = a.State,
                ZipCode = a.ZipCode,
            })
            .ToList();

    /// <summary>Maps the write DTO phones to new <see cref="Phone"/> children.</summary>
    private static List<Phone> MapPhones(IReadOnlyList<PhoneDto>? phones) =>
        (phones ?? Array.Empty<PhoneDto>())
            .Select(p => new Phone
            {
                Id = Guid.NewGuid(),
                Number = p.Number,
                Type = p.Type,
            })
            .ToList();

    /// <summary>Projects a <see cref="Person"/> to its read DTO (never the password hash).</summary>
    private static PersonDto ToDto(Person person) => new(
        person.Id,
        person.Name,
        person.Email,
        person.Role.ToString(),
        person.CreatedAt,
        person.Addresses
            .Select(a => new AddressDto(a.Street, a.City, a.State, a.ZipCode))
            .ToList(),
        person.Phones
            .Select(ph => new PhoneDto(ph.Number, ph.Type))
            .ToList());

    /// <summary>
    /// True for connection/timeout/transient DB failures, so the transactional handlers
    /// let them propagate (503) instead of masking them as a generic 500.
    /// </summary>
    private static bool IsDatabaseUnavailable(Exception ex) =>
        ex is NpgsqlException
        || ex is DbUpdateException { InnerException: NpgsqlException }
        || ex.InnerException is NpgsqlException
        || ex is TimeoutException;
}
