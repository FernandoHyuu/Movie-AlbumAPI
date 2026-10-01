using StreamingPanel.Core.Enums;

namespace StreamingPanel.Core.Validation;

/// <summary>
/// Shared email-format check. An address is well-formed when it has exactly one '@'
/// with text before it and a dotted domain after it (dot neither leading nor trailing).
/// </summary>
internal static class EmailRules
{
    public static bool IsWellFormed(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var parts = email.Split('@');
        if (parts.Length != 2)
        {
            return false;
        }

        var local = parts[0];
        var domain = parts[1];
        if (local.Length == 0 || domain.Length == 0)
        {
            return false;
        }

        var dot = domain.IndexOf('.');
        return dot > 0 && dot < domain.Length - 1;
    }
}

/// <summary>Shared role check. A role string is valid only when it exactly matches a defined <see cref="Role"/> name.</summary>
internal static class RoleRules
{
    public static bool IsKnownRole(string? role)
    {
        // Match by name only. Enum.TryParse would also accept integer-formatted strings
        // (e.g. "0" -> Role.Admin), letting a numeric client value sneak in as a role.
        // A case-sensitive name match keeps only the four defined role names.
        return !string.IsNullOrWhiteSpace(role)
            && Enum.GetNames<Role>().Contains(role, StringComparer.Ordinal);
    }
}
