namespace StreamingPanel.Core.Interfaces;

/// <summary>
/// Password hashing primitive. Only the resulting hash is ever persisted; plaintext
/// passwords never cross this boundary.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes a plaintext password into a self-describing hash (algorithm, salt, and
    /// iteration count embedded) suitable for storage.
    /// </summary>
    string Hash(string password);

    /// <summary>Verifies a plaintext password against a stored hash.</summary>
    bool Verify(string password, string hash);
}
