import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { TokenService } from '../services/token.service';

/**
 * Blocks a protected route when the session is absent, expired, or invalid,
 * redirecting to `/login` with the attempted URL as a `returnUrl` so login can
 * send the user back.
 *
 * Expiry/validity is checked client-side only from the access token; the API
 * remains the authority on token validity.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const tokens = inject(TokenService);
  const router = inject(Router);

  if (hasValidSession(tokens.getAccessToken())) {
    return true;
  }

  // Preserve the attempted URL so login can return the user to it.
  return router.createUrlTree(['/login'], {
    queryParams: { returnUrl: state.url },
  });
};

/**
 * `true` when the access token is present, well-formed, and not expired. A
 * missing or malformed token yields `false`.
 */
function hasValidSession(accessToken: string | null): boolean {
  if (!accessToken) {
    return false;
  }
  const payload = decodeJwtPayload(accessToken);
  if (payload === null) {
    return false;
  }
  return !isExpired(payload);
}

/** `true` when the payload's `exp` claim (seconds since epoch) is in the past. */
function isExpired(payload: Record<string, unknown>): boolean {
  const exp = payload['exp'];
  if (typeof exp !== 'number') {
    // No numeric expiry claim: cannot establish expiry, treat as not expired.
    return false;
  }
  const nowSeconds = Math.floor(Date.now() / 1000);
  return exp <= nowSeconds;
}

/**
 * Decode the payload segment of a JWT without verifying its signature. Returns
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
