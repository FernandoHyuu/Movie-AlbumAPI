namespace StreamingPanel.Core.Enums;

/// <summary>
/// Authorization level for a <see cref="Entities.Person"/>. Persisted as its string
/// name via EF Core value conversion.
/// </summary>
public enum Role
{
    Admin,
    User_Movie,
    User_Album,
    User_Full
}
