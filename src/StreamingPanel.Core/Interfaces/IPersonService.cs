using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Core.Interfaces;

/// <summary>
/// Business rules for the Person aggregate. The 0-10 cardinality on addresses and
/// phones is enforced here as well as in the validator: the validator guards the HTTP
/// edge, the service guards the invariant for any other caller. Delete relies on the EF
/// cascade to remove children in the same transaction, rolling back on any failure.
/// Expected failures come back as a failed <see cref="Result{T}"/>.
/// </summary>
public interface IPersonService
{
    /// <summary>
    /// Returns one page of persons. <paramref name="page"/> is floored to 1 and
    /// <paramref name="pageSize"/> is clamped to 1..50.
    /// </summary>
    Task<Result<PagedResult<PersonDto>>> GetPagedAsync(int page, int pageSize);

    /// <summary>Returns the person, or <see cref="ErrorCode.NotFound"/> when the id is unknown.</summary>
    Task<Result<PersonDto>> GetByIdAsync(Guid id);

    /// <summary>
    /// Creates a person. Rejects more than 10 addresses or phones with
    /// <see cref="ErrorCode.Validation"/>; hashes the supplied password.
    /// </summary>
    Task<Result<PersonDto>> CreateAsync(PersonWriteDto dto);

    /// <summary>
    /// Updates the person (role included), replacing its address and phone collections
    /// in a transaction. Returns <see cref="ErrorCode.NotFound"/> for an unknown id and
    /// <see cref="ErrorCode.Validation"/> when the child cardinality is exceeded.
    /// </summary>
    Task<Result<PersonDto>> UpdateAsync(Guid id, PersonWriteDto dto);

    /// <summary>
    /// Deletes the person; addresses and phones go with it through the EF cascade.
    /// Returns <see cref="ErrorCode.NotFound"/> for an unknown id, or
    /// <see cref="ErrorCode.Internal"/> if the transaction has to roll back.
    /// </summary>
    Task<Result> DeleteAsync(Guid id);
}
