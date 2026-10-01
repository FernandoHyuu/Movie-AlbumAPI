/**
 * Runtime environment configuration.
 *
 * `apiBaseUrl` is the origin/prefix prepended to every API path. It defaults to
 * the relative `/api` so the SPA calls the backend through the same origin it is
 * served from (and any dev-server proxy or reverse proxy in the compose setup),
 * avoiding hard-coded hosts. Services append endpoint paths such as
 * `/auth/login` to this base.
 */
export const environment = {
  production: false,
  apiBaseUrl: '/api',
};
