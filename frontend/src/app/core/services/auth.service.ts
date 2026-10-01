import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  AuthResponse,
  LoginRequest,
  RefreshRequest,
  RegisterRequest,
} from '../models/auth.model';
import { TokenService } from './token.service';

/**
 * Wraps the auth endpoints and keeps {@link TokenService} in sync. `login`,
 * `register`, and `refresh` persist the issued token pair on success; `logout`
 * clears it locally. All methods return cold Observables.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly tokens = inject(TokenService);

  private readonly baseUrl = `${environment.apiBaseUrl}/auth`;

  /** Authenticate with credentials; stores the issued token pair on success. */
  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${this.baseUrl}/login`, request)
      .pipe(tap((response) => this.storeTokens(response)));
  }

  /** Register a new account; stores the issued token pair on success. */
  register(request: RegisterRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${this.baseUrl}/register`, request)
      .pipe(tap((response) => this.storeTokens(response)));
  }

  /**
   * Exchange the refresh token for a new token pair, storing it on success.
   * Used by the interceptor's shared refresh flow.
   */
  refresh(refreshToken: string): Observable<AuthResponse> {
    const body: RefreshRequest = { refreshToken };
    return this.http
      .post<AuthResponse>(`${this.baseUrl}/refresh-token`, body)
      .pipe(tap((response) => this.storeTokens(response)));
  }

  /** Clear the stored session locally so the user is signed out. */
  logout(): void {
    this.tokens.clear();
  }

  private storeTokens(response: AuthResponse): void {
    this.tokens.setTokens(response.accessToken, response.refreshToken);
  }
}
