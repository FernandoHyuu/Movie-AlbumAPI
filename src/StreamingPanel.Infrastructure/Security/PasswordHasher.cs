using Microsoft.AspNetCore.Identity;
using StreamingPanel.Core.Entities;
using StreamingPanel.Core.Interfaces;

namespace StreamingPanel.Infrastructure.Security;

/// <summary>
/// <see cref="IPasswordHasher"/> backed by ASP.NET Core Identity's
/// <see cref="PasswordHasher{TUser}"/> (PBKDF2 with a per-password salt). Produces only
/// the encoded hash for storage; the plaintext is never persisted.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    // The Identity hasher takes a TUser argument for interface shape only; it does
    // not read any state from it, so a single shared throwaway instance is safe.
    private static readonly Person HashingSubject = new();

    private readonly PasswordHasher<Person> _inner = new();

    /// <inheritdoc />
    public string Hash(string password) => _inner.HashPassword(HashingSubject, password);

    /// <inheritdoc />
    public bool Verify(string password, string hash)
    {
        var result = _inner.VerifyHashedPassword(HashingSubject, hash, password);
        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
