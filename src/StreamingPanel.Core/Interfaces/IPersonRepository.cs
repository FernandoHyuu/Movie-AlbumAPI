using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Entities;

namespace StreamingPanel.Core.Interfaces;

/// <summary>
/// Persistence boundary for the <see cref="Person"/> aggregate. Read projections go
/// through <see cref="PersonDto"/> so the password hash is never materialized into a
/// read shape. Mutations are staged on the tracked context and only hit the database
/// on <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IPersonRepository
{
    /// <summary>Returns one page of persons ordered by name plus the total matching count.</summary>
    Task<(IReadOnlyList<PersonDto> Items, int TotalCount)> GetPagedAsync(int skip, int take);

    /// <summary>Returns the person as a <see cref="PersonDto"/> (no password hash), or null if not found.</summary>
    Task<PersonDto?> GetByIdAsync(Guid id);

    /// <summary>
    /// Returns the tracked entity with addresses and phones loaded, for updates and
    /// cascade deletes. Null when not found.
    /// </summary>
    Task<Person?> GetEntityAsync(Guid id);

    /// <summary>Case-insensitive check for whether an email is already taken.</summary>
    Task<bool> EmailExistsAsync(string email);

    Task AddAsync(Person person);

    void Update(Person person);

    void Remove(Person person);

    Task SaveChangesAsync();
}
