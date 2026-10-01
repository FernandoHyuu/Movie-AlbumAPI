using Microsoft.EntityFrameworkCore;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Interfaces;
using StreamingPanel.Infrastructure.Persistence;

namespace StreamingPanel.Infrastructure.Repositories;

/// <summary>
/// EF Core <see cref="IPersonRepository"/> over <see cref="AppDbContext"/>. DTO reads
/// project identity, role, timestamps, and child addresses/phones but never the password
/// hash. <see cref="GetEntityAsync"/> eager-loads children so the service can replace or
/// cascade them within one transaction.
/// </summary>
public sealed class PersonRepository : IPersonRepository
{
    private readonly AppDbContext _db;

    public PersonRepository(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<PersonDto> Items, int TotalCount)> GetPagedAsync(int skip, int take)
    {
        var totalCount = await _db.Persons.CountAsync();

        var items = await _db.Persons
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip(skip)
            .Take(take)
            .Select(p => new PersonDto(
                p.Id,
                p.Name,
                p.Email,
                p.Role.ToString(),
                p.CreatedAt,
                p.Addresses
                    .Select(a => new AddressDto(a.Street, a.City, a.State, a.ZipCode))
                    .ToList(),
                p.Phones
                    .Select(ph => new PhoneDto(ph.Number, ph.Type))
                    .ToList()))
            .ToListAsync();

        return (items, totalCount);
    }

    /// <inheritdoc />
    public Task<PersonDto?> GetByIdAsync(Guid id) =>
        _db.Persons
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PersonDto(
                p.Id,
                p.Name,
                p.Email,
                p.Role.ToString(),
                p.CreatedAt,
                p.Addresses
                    .Select(a => new AddressDto(a.Street, a.City, a.State, a.ZipCode))
                    .ToList(),
                p.Phones
                    .Select(ph => new PhoneDto(ph.Number, ph.Type))
                    .ToList()))
            .SingleOrDefaultAsync();

    /// <inheritdoc />
    public Task<Person?> GetEntityAsync(Guid id) =>
        _db.Persons
            .Include(p => p.Addresses)
            .Include(p => p.Phones)
            .SingleOrDefaultAsync(p => p.Id == id);

    /// <inheritdoc />
    public Task<bool> EmailExistsAsync(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        var normalized = email.Trim();
        return _db.Persons.AnyAsync(p => p.Email.ToLower() == normalized.ToLower());
    }

    /// <inheritdoc />
    public async Task AddAsync(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        await _db.Persons.AddAsync(person);
    }

    /// <inheritdoc />
    public void Update(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        _db.Persons.Update(person);
    }

    /// <inheritdoc />
    public void Remove(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        _db.Persons.Remove(person);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
