using FsCheck;
using FsCheck.Xunit;
using StreamingPanel.Core.Interfaces;
using StreamingPanel.Infrastructure.Security;

namespace StreamingPanel.Tests.Security;

/// <summary>
/// Property-based tests for <see cref="PasswordHasher"/>.
///
/// Feature: streaming-panel, Property 1: Password hashing never stores plaintext and verifies correctly
///
/// For any valid password string, after it is hashed the stored hash is not equal
/// to (nor embeds) the plaintext, verifying the original password against that hash
/// succeeds, and verifying any different password against it fails.
///
/// Validates: Requirements 1.2, 11.2
/// </summary>
public class PasswordHasherPropertyTests
{
    private readonly IPasswordHasher _hasher = new PasswordHasher();

    /// <summary>
    /// Hashing never yields the plaintext (nor embeds it), the original password
    /// verifies against its own hash, and a different password does not.
    ///
    /// Feature: streaming-panel, Property 1: Password hashing never stores plaintext and verifies correctly
    /// Validates: Requirements 1.2, 11.2
    /// </summary>
    [Property(Arbitrary = new[] { typeof(PasswordArbitraries) }, MaxTest = 100)]
    public Property HashNeverStoresPlaintextAndVerifiesCorrectly(string password, string other)
    {
        // Ensure the two passwords differ; if the generator produced equal values,
        // derive a guaranteed-different "wrong" password.
        var wrongPassword = string.Equals(password, other, StringComparison.Ordinal)
            ? password + "\u0001difference"
            : other;

        var hash = _hasher.Hash(password);

        var hashIsNotPlaintext = hash != password;
        var hashDoesNotContainPlaintext = !hash.Contains(password, StringComparison.Ordinal);
        var originalVerifies = _hasher.Verify(password, hash);
        var wrongDoesNotVerify = !_hasher.Verify(wrongPassword, hash);

        return (hashIsNotPlaintext && hashDoesNotContainPlaintext && originalVerifies && wrongDoesNotVerify)
            .ToProperty()
            .Label($"hash='{hash}', pwd len={password.Length}, " +
                   $"notPlaintext={hashIsNotPlaintext}, notContains={hashDoesNotContainPlaintext}, " +
                   $"originalVerifies={originalVerifies}, wrongRejected={wrongDoesNotVerify}");
    }
}

/// <summary>
/// FsCheck arbitrary registrations for the password hashing property.
/// </summary>
public static class PasswordArbitraries
{
    /// <summary>
    /// Realistic password strings: at least 8 characters long and non-whitespace-only.
    ///
    /// The lower bound of 8 reflects the valid-password input space (realistic
    /// minimum length) and, importantly, keeps the "hash does not embed the
    /// plaintext" assertion meaningful: the Identity hasher emits a Base64-encoded
    /// hash, so trivially short strings (e.g. a single "a") can appear in the
    /// encoding purely by coincidence of the Base64 alphabet, which does not
    /// constitute storing the plaintext. Constraining the generator to realistic
    /// passwords tests the real security property without false positives.
    /// </summary>
    public static Arbitrary<string> String() =>
        Arb.Default.String()
            .Filter(s => s is { Length: >= 8 } && !string.IsNullOrWhiteSpace(s));
}
