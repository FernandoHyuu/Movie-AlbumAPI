import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import * as fc from 'fast-check';

import { environment } from '../../../environments/environment';
import { AuthResponse } from '../models/auth.model';
import { TokenService } from '../services/token.service';
import { authInterceptor } from './auth.interceptor';

/**
 * Property tests for the functional {@link authInterceptor} (task 9.2).
 *
 * - Property 28: when a token is present, every protected outgoing request
 *   carries `Authorization: Bearer <token>` (R14.1).
 * - Property 29: N concurrent protected requests that all receive 401 trigger
 *   exactly one shared refresh; once it resolves, every queued request replays
 *   carrying the NEW bearer token (R14.6).
 *
 * The interceptor keeps module-level state (`isRefreshing` + a `BehaviorSubject`
 * gate). Each fast-check run reconfigures TestBed for a fresh injector/token
 * state and fully drains the refresh cycle so no state leaks between runs.
 */

const API = environment.apiBaseUrl;
const AUTH_BASE = `${API}/auth`;
const RUNS = 100;

/** Router stub so navigation calls (`/login`) never throw during a run. */
class RouterStub {
  navigate = jasmine.createSpy('navigate').and.resolveTo(true);
}

function makeAuthResponse(accessToken: string, refreshToken: string): AuthResponse {
  return {
    accessToken,
    refreshToken,
    role: 'Admin',
    accessTokenExpiresAt: '2025-01-01T00:15:00Z',
  };
}

/** Spin up a fresh interceptor-wired TestBed and return the pieces a run needs. */
function setupRun(): {
  http: HttpClient;
  httpMock: HttpTestingController;
  tokens: TokenService;
} {
  localStorage.clear();
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(),
      { provide: Router, useClass: RouterStub },
    ],
  });
  return {
    http: TestBed.inject(HttpClient),
    httpMock: TestBed.inject(HttpTestingController),
    tokens: TestBed.inject(TokenService),
  };
}

/** A protected (non-auth) relative path arbitrary. */
const protectedPathArb = fc
  .array(
    fc
      .string({ minLength: 1, maxLength: 10 })
      .map((s) => s.replace(/[^a-zA-Z0-9]/g, ''))
      .filter((s) => s.length > 0),
    { minLength: 1, maxLength: 3 },
  )
  .map((segments) => `${API}/${segments.join('/')}`)
  // Keep clear of anonymous auth endpoints that bypass the interceptor.
  .filter((url) => !/\/auth\/(login|register|refresh-token)/.test(url));

/** A non-empty token-ish string arbitrary. */
const tokenArb = fc
  .string({ minLength: 1, maxLength: 40 })
  .map((s) => s.replace(/\s/g, ''))
  .filter((s) => s.length > 0);

describe('authInterceptor property tests', () => {
  afterEach(() => {
    localStorage.clear();
  });

  it('Property 28: attaches the bearer token when a token is present (R14.1)', () => {
    // Validates: Requirements 14.1
    fc.assert(
      fc.property(tokenArb, tokenArb, protectedPathArb, (accessToken, refreshToken, url) => {
        const { http, httpMock, tokens } = setupRun();

        tokens.setTokens(accessToken, refreshToken);

        http.get(url).subscribe({ next: () => undefined, error: () => undefined });

        const req = httpMock.expectOne(url);
        expect(req.request.headers.get('Authorization')).toBe(`Bearer ${accessToken}`);

        // Drain: resolve the request so nothing stays pending.
        req.flush({});
        httpMock.verify();
      }),
      { numRuns: RUNS },
    );
  });

  it('Property 29: concurrent 401s trigger one refresh and replay with the new token (R14.6)', () => {
    // Validates: Requirements 14.6
    fc.assert(
      fc.property(
        fc.integer({ min: 2, max: 6 }),
        tokenArb,
        tokenArb,
        tokenArb,
        (concurrency, staleAccess, storedRefresh, newAccess) => {
          // The new token must differ from the stale one so the replay assertion is meaningful.
          fc.pre(newAccess !== staleAccess);

          const { http, httpMock, tokens } = setupRun();
          tokens.setTokens(staleAccess, storedRefresh);

          const url = `${API}/protected/resource`;
          const completed: boolean[] = [];

          // Fire N concurrent protected requests.
          for (let i = 0; i < concurrency; i++) {
            http.get(url).subscribe({
              next: () => completed.push(true),
              error: () => completed.push(false),
            });
          }

          // All N initial requests go out carrying the stale bearer token.
          const initial = httpMock.match(url);
          expect(initial.length).toBe(concurrency);
          initial.forEach((r) =>
            expect(r.request.headers.get('Authorization')).toBe(`Bearer ${staleAccess}`),
          );

          // Each gets a 401; the first starts the refresh, the rest queue.
          initial.forEach((r) =>
            r.flush({ detail: 'expired' }, { status: 401, statusText: 'Unauthorized' }),
          );

          // Exactly ONE refresh request is issued despite N concurrent 401s.
          const refresh = httpMock.expectOne(`${AUTH_BASE}/refresh-token`);
          expect(refresh.request.method).toBe('POST');
          expect(refresh.request.body).toEqual({ refreshToken: storedRefresh });
          refresh.flush(makeAuthResponse(newAccess, storedRefresh));

          // Every queued request replays once, carrying the NEW bearer token.
          const replays = httpMock.match(url);
          expect(replays.length).toBe(concurrency);
          replays.forEach((r) =>
            expect(r.request.headers.get('Authorization')).toBe(`Bearer ${newAccess}`),
          );
          replays.forEach((r) => r.flush({}));

          // Drain fully so module-level refresh state does not leak into the next run.
          expect(completed.length).toBe(concurrency);
          httpMock.verify();
        },
      ),
      { numRuns: RUNS },
    );
  });
});
