using StreamingPanel.Core.Enums;

namespace StreamingPanel.Core.Entities;

/// <summary>
/// A system account holding identity, credentials, role, addresses, phones, and
/// refresh tokens.
/// </summary>
public class Person
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public Role Role { get; set; }

    // Stored in UTC.
    public DateTime CreatedAt { get; set; }

    public ICollection<Address> Addresses { get; set; } = new List<Address>();
    public ICollection<Phone> Phones { get; set; } = new List<Phone>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
