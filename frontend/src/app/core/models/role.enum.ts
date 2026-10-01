/**
 * Access roles governing catalog and admin permissions. Each value matches the
 * string the API embeds in the access-token `role` claim (e.g. `"Admin"`,
 * `"User_Movie"`), so the raw claim can be compared directly against a member.
 */
export enum Role {
  Admin = 'Admin',
  User_Movie = 'User_Movie',
  User_Album = 'User_Album',
  User_Full = 'User_Full',
}

/** The set of recognized role names, used to validate a decoded claim. */
const KNOWN_ROLES = new Set<string>(Object.values(Role));

/**
 * Narrows an arbitrary string to a {@link Role}, or `null` when it names no
 * defined role. Guards and the sidebar treat `null` as unauthorized.
 */
export function toRole(value: string | null | undefined): Role | null {
  return value != null && KNOWN_ROLES.has(value) ? (value as Role) : null;
}
