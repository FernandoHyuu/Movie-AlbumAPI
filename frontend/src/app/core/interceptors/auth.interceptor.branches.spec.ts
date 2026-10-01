import {
  HttpClient,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Router } from '@angular/router';

import { environment } from '../../../environments/environment';
import { AuthResponse } from '../models/auth.model';
import { AuthService } from '../services/auth.service';
import { TokenService } from '../services/token.service';
import { authInterceptor } from './auth.interceptor';

/**
 * Example (non-property) tests for the branches of {@link authInterceptor}.
 *
 * These complement the property tests (task 9.2) with explicit, single-scenario
 * assertions over the behaviours the acceptance criteria call out:
 *   - R14.2 no-token redirect (no request is sent)
 *   - R14.4 retry-once with the refreshed token
 *   - R14.5 refresh-401 clears tokens and routes to login
 *   - R14.7 refresh network error fails the request but preserves tokens
 */

const API = environment.apiBaseUrl;
const PROTECTED_URL = `${API}/movies`;
const REFRESH_URL = `${API}/auth/refresh-token`;

function makeAuthResponse(overrides: Partial<AuthResponse> = {}): AuthResponse {
  return {
    accessToken: 'new-access-token',
    refreshToken: 'new-refresh-token',
    role: 'Admin',
    accessTokenExpiresAt: '2025-01-01T00:15:00Z',
    ...overrides,
  };
}

describe('authInterceptor branches', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let tokens: TokenService;
  let router: Router;
  let navigateSpy: jasmine.Spy;

  beforeEach(() => {
    localStorage.clear();

    const routerStub: Pick<Router, 'navigate'> = {
      navigate: jasmine.createSpy('navigate').and.resolveTo(true),
    };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        AuthService,
        TokenService,
        { provide: Router, useValue: routerStub },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    tokens = TestBed.inject(TokenService);
    router = TestBed.inject(Router);
    navigateSpy = router.navigate as jasmine.Spy;
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  // R14.2 — protected request with no token: route to login, send nothing.
  it('routes to /login and sends no request when no access token is present', () => {
    expect(tokens.getAccessToken()).toBeNull();

    let erroredWith: unknown;
    http.get(PROTECTED_URL).subscribe({
      next: () => fail('expected the request to error without being sent'),
      error: (err: unknown) => (erroredWith = err),
    });

    expect(navigateSpy).toHaveBeenCalledOnceWith(['/login']);
    expect(erroredWith).toEqual(jasmine.any(Error));
    // No HTTP request should have left the interceptor.
    httpMock.expectNone(PROTECTED_URL);
  });

  // R14.4 — on 401, refresh then retry the original request once with the new token.
  it('retries the original request once with the refreshed bearer token after a 401', () => {
    tokens.setTokens('expired-access-token', 'stored-refresh-token');

    let result: unknown;
    http.get(PROTECTED_URL).subscribe((res) => (result = res));

    // Original request carries the (expired) token and gets a 401.
    const first = httpMock.expectOne(PROTECTED_URL);
    expect(first.request.headers.get('Authorization')).toBe('Bearer expired-access-token');
    first.flush({ detail: 'expired' }, { status: 401, statusText: 'Unauthorized' });

    // Interceptor refreshes using the stored refresh token.
    const refresh = httpMock.expectOne(REFRESH_URL);
    expect(refresh.request.method).toBe('POST');
    expect(refresh.request.body).toEqual({ refreshToken: 'stored-refresh-token' });
    refresh.flush(makeAuthResponse({ accessToken: 'fresh-access-token' }));

    // Exactly one retry, carrying the fresh token.
    const retried = httpMock.expectOne(PROTECTED_URL);
    expect(retried.request.headers.get('Authorization')).toBe('Bearer fresh-access-token');
    retried.flush({ ok: true });

    expect(result).toEqual({ ok: true });
    // Only the single retry happened — no further requests queued.
    httpMock.expectNone(PROTECTED_URL);
  });

  // R14.5 — refresh itself returns 401: clear tokens and route to login.
  it('clears tokens and routes to /login when the refresh request returns 401', () => {
    tokens.setTokens('expired-access-token', 'stored-refresh-token');

    let erroredWith: HttpErrorResponse | undefined;
    http.get(PROTECTED_URL).subscribe({
      next: () => fail('expected the request to fail after a failed refresh'),
      error: (err: HttpErrorResponse) => (erroredWith = err),
    });

    httpMock
      .expectOne(PROTECTED_URL)
      .flush({ detail: 'expired' }, { status: 401, statusText: 'Unauthorized' });

    httpMock
      .expectOne(REFRESH_URL)
      .flush({ detail: 'refresh rejected' }, { status: 401, statusText: 'Unauthorized' });

    expect(erroredWith?.status).toBe(401);
    expect(tokens.getAccessToken()).toBeNull();
    expect(tokens.getRefreshToken()).toBeNull();
    expect(navigateSpy).toHaveBeenCalledOnceWith(['/login']);
    // The original request is not retried after a failed refresh.
    httpMock.expectNone(PROTECTED_URL);
  });

  // R14.7 — refresh fails on a network error: fail the request, keep tokens, no logout/redirect.
  it('fails the request and preserves tokens when the refresh errors on the network', () => {
    tokens.setTokens('expired-access-token', 'stored-refresh-token');

    let erroredWith: HttpErrorResponse | undefined;
    http.get(PROTECTED_URL).subscribe({
      next: () => fail('expected the request to fail on a refresh network error'),
      error: (err: HttpErrorResponse) => (erroredWith = err),
    });

    httpMock
      .expectOne(PROTECTED_URL)
      .flush({ detail: 'expired' }, { status: 401, statusText: 'Unauthorized' });

    // Refresh fails with a transport-level error (not a 401).
    httpMock
      .expectOne(REFRESH_URL)
      .error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    expect(erroredWith).toBeDefined();
    expect(erroredWith?.status).not.toBe(401);
    // Tokens are preserved on a network failure (R14.7).
    expect(tokens.getAccessToken()).toBe('expired-access-token');
    expect(tokens.getRefreshToken()).toBe('stored-refresh-token');
    // No logout-driven redirect occurred.
    expect(navigateSpy).not.toHaveBeenCalled();
    // The original request is not retried after a network failure.
    httpMock.expectNone(PROTECTED_URL);
  });

  // R14.7 — refresh exceeds the 5 s budget: times out, fails the request, keeps tokens.
  it('times out the refresh after 5 s, fails the request, and preserves tokens', fakeAsync(() => {
    tokens.setTokens('expired-access-token', 'stored-refresh-token');

    let erroredWith: unknown;
    http.get(PROTECTED_URL).subscribe({
      next: () => fail('expected the request to fail when the refresh times out'),
      error: (err: unknown) => (erroredWith = err),
    });

    httpMock
      .expectOne(PROTECTED_URL)
      .flush({ detail: 'expired' }, { status: 401, statusText: 'Unauthorized' });

    // Refresh request is opened but never responds.
    const refresh = httpMock.expectOne(REFRESH_URL);

    // Advance past the 5 s refresh budget to trip the timeout.
    tick(5000);

    expect(erroredWith).toBeDefined();
    // The timeout cancels the in-flight refresh rather than awaiting a response.
    expect(refresh.cancelled).toBeTrue();
    // Tokens remain intact after a timeout (R14.7).
    expect(tokens.getAccessToken()).toBe('expired-access-token');
    expect(tokens.getRefreshToken()).toBe('stored-refresh-token');
    expect(navigateSpy).not.toHaveBeenCalled();
  }));
});
