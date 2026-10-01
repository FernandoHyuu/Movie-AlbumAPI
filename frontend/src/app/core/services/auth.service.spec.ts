import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';

import { environment } from '../../../environments/environment';
import { AuthResponse } from '../models/auth.model';
import { AuthService } from './auth.service';
import { TokenService } from './token.service';

const AUTH_BASE = `${environment.apiBaseUrl}/auth`;

function makeResponse(overrides: Partial<AuthResponse> = {}): AuthResponse {
  return {
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    role: 'Admin',
    accessTokenExpiresAt: '2025-01-01T00:15:00Z',
    ...overrides,
  };
}

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;
  let tokens: TokenService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
    tokens = TestBed.inject(TokenService);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('posts credentials to the login endpoint and stores the token pair', () => {
    const body = { email: 'a@b.com', password: 'password123' };
    const response = makeResponse({ accessToken: 'acc', refreshToken: 'ref' });

    service.login(body).subscribe((result) => {
      expect(result).toEqual(response);
    });

    const req = httpMock.expectOne(`${AUTH_BASE}/login`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(response);

    expect(tokens.getAccessToken()).toBe('acc');
    expect(tokens.getRefreshToken()).toBe('ref');
  });

  it('posts to the register endpoint and stores the token pair', () => {
    const body = {
      email: 'a@b.com',
      password: 'password123',
      role: 'User_Movie',
      name: 'Ana',
    };
    const response = makeResponse({ accessToken: 'r-acc', refreshToken: 'r-ref' });

    service.register(body).subscribe((result) => {
      expect(result).toEqual(response);
    });

    const req = httpMock.expectOne(`${AUTH_BASE}/register`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(response);

    expect(tokens.getAccessToken()).toBe('r-acc');
    expect(tokens.getRefreshToken()).toBe('r-ref');
  });

  it('posts the refresh token to the refresh endpoint and stores the new pair', () => {
    const response = makeResponse({ accessToken: 'new-acc', refreshToken: 'new-ref' });

    service.refresh('old-refresh').subscribe((result) => {
      expect(result).toEqual(response);
    });

    const req = httpMock.expectOne(`${AUTH_BASE}/refresh-token`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ refreshToken: 'old-refresh' });
    req.flush(response);

    expect(tokens.getAccessToken()).toBe('new-acc');
    expect(tokens.getRefreshToken()).toBe('new-ref');
  });

  it('does not store tokens when login fails', () => {
    service.login({ email: 'a@b.com', password: 'bad' }).subscribe({
      next: () => fail('expected an error'),
      error: (err) => expect(err.status).toBe(401),
    });

    httpMock
      .expectOne(`${AUTH_BASE}/login`)
      .flush({ detail: 'Invalid credentials' }, { status: 401, statusText: 'Unauthorized' });

    expect(tokens.getAccessToken()).toBeNull();
    expect(tokens.getRefreshToken()).toBeNull();
  });

  it('clears the stored session on logout', () => {
    tokens.setTokens('acc', 'ref');
    expect(tokens.getAccessToken()).toBe('acc');

    service.logout();

    expect(tokens.getAccessToken()).toBeNull();
    expect(tokens.getRefreshToken()).toBeNull();
  });
});
