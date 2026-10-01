import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, Observable, filter, switchMap, take, throwError, timeout } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { AuthService } from '../services/auth.service';
import { TokenService } from '../services/token.service';

/**
 * Attaches the access token to protected requests and refreshes the session on
 * a 401. Anonymous auth endpoints pass through untouched; a protected request
 * with no token is redirected to `/login` without being sent.
 *
 * The refresh is gated by a single module-level BehaviorSubject so that when
 * several requests hit a 401 at once, only the first triggers a refresh and the
 * rest queue for the new token — avoiding a storm of simultaneous refreshes.
 */

/** Refresh timeout budget in milliseconds. */
const REFRESH_TIMEOUT_MS = 5000;

/**
 * Shared refresh gate. `null` means no refresh is in progress; a non-null string
 * is the freshly issued access token that queued requests replay with.
 */
const refreshToken$ = new BehaviorSubject<string | null>(null);

/** Tracks whether a shared refresh is currently in flight. */
let isRefreshing = false;

/** Auth endpoint path fragments that are anonymous and must bypass the interceptor. */
const ANONYMOUS_AUTH_PATHS = ['/auth/login', '/auth/register', '/auth/refresh-token'];

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const tokens = inject(TokenService);
  const auth = inject(AuthService);
  const router = inject(Router);

  // Anonymous auth endpoints bypass token attachment and the refresh flow.
  if (isAnonymousAuthRequest(req)) {
    return next(req);
  }

  const accessToken = tokens.getAccessToken();

  // Protected request without a token: route to login and do not send.
  if (!accessToken) {
    void router.navigate(['/login']);
    return throwError(() => new Error('No access token; routing to login.'));
  }

  return next(withBearer(req, accessToken)).pipe(
    catchError((error: unknown) => {
      if (isUnauthorized(error)) {
        return handle401(req, next, tokens, auth, router);
      }
      return throwError(() => error);
    }),
  );
};

/** Handle a `401` on a protected request by refreshing (or queuing) and replaying. */
function handle401(
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  tokens: TokenService,
  auth: AuthService,
  router: Router,
): Observable<HttpEvent<unknown>> {
  // A refresh is already in flight: queue until the new token emits.
  if (isRefreshing) {
    return refreshToken$.pipe(
      filter((token): token is string => token !== null),
      take(1),
      switchMap((token) => next(withBearer(req, token))),
    );
  }

  const storedRefreshToken = tokens.getRefreshToken();
  if (!storedRefreshToken) {
    // No refresh token to attempt a refresh with: clear and route to login.
    auth.logout();
    void router.navigate(['/login']);
    return throwError(() => new Error('No refresh token available; routing to login.'));
  }

  // Start a new shared refresh; concurrent requests gate on the subject.
  isRefreshing = true;
  refreshToken$.next(null);

  return auth.refresh(storedRefreshToken).pipe(
    timeout(REFRESH_TIMEOUT_MS),
    switchMap((response) => {
      // Refresh succeeded: release queued requests and retry the original once.
      isRefreshing = false;
      refreshToken$.next(response.accessToken);
      return next(withBearer(req, response.accessToken));
    }),
    catchError((error: unknown) => {
      isRefreshing = false;

      if (isUnauthorized(error)) {
        // Refresh rejected: clear tokens and route to login.
        auth.logout();
        void router.navigate(['/login']);
        return throwError(() => error);
      }

      // Network error or timeout: fail the request, keep tokens, show a toast.
      showRefreshErrorToast();
      return throwError(() => error);
    }),
  );
}

/** Clone a request with the `Authorization: Bearer` header applied. */
function withBearer(req: HttpRequest<unknown>, accessToken: string): HttpRequest<unknown> {
  return req.clone({
    setHeaders: { Authorization: `Bearer ${accessToken}` },
  });
}

/** `true` when the request targets an anonymous auth endpoint. */
function isAnonymousAuthRequest(req: HttpRequest<unknown>): boolean {
  return ANONYMOUS_AUTH_PATHS.some((path) => req.url.includes(path));
}

/** `true` when the error is an HTTP `401 Unauthorized` response. */
function isUnauthorized(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 401;
}

/**
 * Shows an error toast when a refresh fails on network error/timeout.
 *
 * TODO: wire this to `NotificationService.error(...)`. It's isolated behind this
 * helper so the interceptor doesn't depend on NotificationService yet; the
 * failure still propagates to the caller regardless.
 */
function showRefreshErrorToast(): void {
  // No-op until NotificationService is wired in.
}
