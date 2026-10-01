/**
 * Request/response models for the `/api/auth/*` endpoints. The `role` on
 * `AuthResponse` is the raw claim string; `TokenService` derives the typed
 * {@link Role} from the access token itself.
 */

/** Credentials submitted to `POST /api/auth/login`. */
export interface LoginRequest {
  email: string;
  password: string;
}

/** Payload submitted to `POST /api/auth/register`. */
export interface RegisterRequest {
  email: string;
  password: string;
  role: string;
  name?: string | null;
}

/** Payload submitted to `POST /api/auth/refresh-token`. */
export interface RefreshRequest {
  refreshToken: string;
}

/**
 * Token pair plus role returned by the login, register, and refresh endpoints.
 * `accessTokenExpiresAt` is an ISO-8601 timestamp.
 */
export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  role: string;
  accessTokenExpiresAt: string;
}
