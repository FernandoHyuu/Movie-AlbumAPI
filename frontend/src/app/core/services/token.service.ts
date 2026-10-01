import { Injectable, Signal, computed, signal } from '@angular/core';
import { Role, toRole } from '../models/role.enum';

const ACCESS_TOKEN_KEY = 'sp.accessToken';
const REFRESH_TOKEN_KEY = 'sp.refreshToken';

/**
 * Holds the access and refresh tokens and exposes the decoded role as a signal.
 * Tokens persist in `localStorage` so the session survives a reload. The role
 * is read from the access token's `role` claim; an absent, malformed, or
 * unrecognized claim yields `null`, which the UI treats as unauthorized.
 */
@Injectable({ providedIn: 'root' })
export class TokenService {
  private readonly accessToken = signal<string | null>(readStorage(ACCESS_TOKEN_KEY));
  private readonly refreshToken = signal<string | null>(readStorage(REFRESH_TOKEN_KEY));

  /** The current access token, or `null` when no session is stored. */
  readonly accessToken$: Signal<string | null> = this.accessToken.asReadonly();

  /** The current refresh token, or `null` when no session is stored. */
  readonly refreshToken$: Signal<string | null> = this.refreshToken.asReadonly();

  /**
   * The role decoded from the access-token `role` claim, or `null` when absent
   * or unrecognized. Guards and the sidebar derive navigation from this signal.
   */
  readonly role: Signal<Role | null> = computed(() =>
    decodeRole(this.accessToken()),
  );

  /** `true` when an access token is currently stored. */
  readonly hasAccessToken: Signal<boolean> = computed(() => this.accessToken() !== null);

  /** Persist a freshly issued token pair and refresh the derived role. */
  setTokens(accessToken: string, refreshToken: string): void {
    writeStorage(ACCESS_TOKEN_KEY, accessToken);
    writeStorage(REFRESH_TOKEN_KEY, refreshToken);
    this.accessToken.set(accessToken);
    this.refreshToken.set(refreshToken);
  }

  /** Clear both tokens from storage and state (e.g. on logout or refresh failure). */
  clear(): void {
    removeStorage(ACCESS_TOKEN_KEY);
    removeStorage(REFRESH_TOKEN_KEY);
    this.accessToken.set(null);
    this.refreshToken.set(null);
  }

  /** Read the current access token synchronously (for the HTTP interceptor). */
  getAccessToken(): string | null {
    return this.accessToken();
  }

  /** Read the current refresh token synchronously (for the refresh flow). */
  getRefreshToken(): string | null {
    return this.refreshToken();
  }
}

/** Decode the `role` claim from a JWT access token, returning a known {@link Role} or `null`. */
function decodeRole(accessToken: string | null): Role | null {
  if (!accessToken) {
    return null;
  }
  const payload = decodeJwtPayload(accessToken);
  const claim = payload?.['role'];
  return typeof claim === 'string' ? toRole(claim) : null;
}

/**
 * Decode a JWT payload without verifying its signature — the client only reads
 * the role claim for UI purposes; the API is the authority on validity. Returns
 * `null` for any malformed token.
 */
function decodeJwtPayload(token: string): Record<string, unknown> | null {
  const parts = token.split('.');
  if (parts.length !== 3) {
    return null;
  }
  try {
    const json = base64UrlDecode(parts[1]);
    const parsed: unknown = JSON.parse(json);
    return typeof parsed === 'object' && parsed !== null
      ? (parsed as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}

/** Decode a base64url-encoded string to UTF-8 text. */
function base64UrlDecode(segment: string): string {
  let base64 = segment.replace(/-/g, '+').replace(/_/g, '/');
  const padding = base64.length % 4;
  if (padding) {
    base64 += '='.repeat(4 - padding);
  }
  const binary = atob(base64);
  const bytes = Uint8Array.from(binary, (char) => char.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}

function readStorage(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStorage(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Storage may be unavailable (private mode / quota); state still holds in memory.
  }
}

function removeStorage(key: string): void {
  try {
    localStorage.removeItem(key);
  } catch {
    // Ignore storage errors; in-memory state is cleared regardless.
  }
}
