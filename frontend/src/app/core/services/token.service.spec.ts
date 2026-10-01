import { TestBed } from '@angular/core/testing';
import { Role } from '../models/role.enum';
import { TokenService } from './token.service';

/** Build an unsigned JWT (header.payload.signature) carrying the given payload. */
function makeJwt(payload: Record<string, unknown>): string {
  const encode = (obj: unknown) =>
    btoa(JSON.stringify(obj)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode(payload)}.sig`;
}

describe('TokenService', () => {
  let service: TokenService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    service = TestBed.inject(TokenService);
  });

  afterEach(() => localStorage.clear());

  it('starts with no tokens and a null role', () => {
    expect(service.getAccessToken()).toBeNull();
    expect(service.getRefreshToken()).toBeNull();
    expect(service.hasAccessToken()).toBe(false);
    expect(service.role()).toBeNull();
  });

  it('persists the token pair to localStorage', () => {
    const access = makeJwt({ role: 'Admin' });
    service.setTokens(access, 'refresh-abc');

    expect(service.getAccessToken()).toBe(access);
    expect(service.getRefreshToken()).toBe('refresh-abc');
    expect(localStorage.getItem('sp.accessToken')).toBe(access);
    expect(localStorage.getItem('sp.refreshToken')).toBe('refresh-abc');
  });

  it('exposes the decoded role claim as a signal', () => {
    service.setTokens(makeJwt({ role: 'User_Full' }), 'r');
    expect(service.role()).toBe(Role.User_Full);
  });

  it('yields a null role for an unrecognized claim', () => {
    service.setTokens(makeJwt({ role: 'Superuser' }), 'r');
    expect(service.role()).toBeNull();
  });

  it('yields a null role when the role claim is absent', () => {
    service.setTokens(makeJwt({ sub: '123' }), 'r');
    expect(service.role()).toBeNull();
  });

  it('yields a null role for a malformed token', () => {
    service.setTokens('not-a-jwt', 'r');
    expect(service.role()).toBeNull();
  });

  it('clears tokens and resets the role', () => {
    service.setTokens(makeJwt({ role: 'Admin' }), 'r');
    service.clear();

    expect(service.getAccessToken()).toBeNull();
    expect(service.role()).toBeNull();
    expect(localStorage.getItem('sp.accessToken')).toBeNull();
    expect(localStorage.getItem('sp.refreshToken')).toBeNull();
  });

  it('hydrates tokens from localStorage on construction', () => {
    const access = makeJwt({ role: 'User_Movie' });
    localStorage.setItem('sp.accessToken', access);
    localStorage.setItem('sp.refreshToken', 'r');

    // Reset the TestBed so a brand-new TokenService instance is constructed,
    // reading the seeded values from storage.
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    const fresh = TestBed.inject(TokenService);

    expect(fresh.getAccessToken()).toBe(access);
    expect(fresh.getRefreshToken()).toBe('r');
    expect(fresh.role()).toBe(Role.User_Movie);
  });
});
