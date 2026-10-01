import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { Role } from '../models/role.enum';
import { TokenService } from '../services/token.service';

/** Route path the role guard redirects to when access is denied. */
const UNAUTHORIZED_FALLBACK = '/unauthorized';

/**
 * Authorization guard parameterized by the roles permitted for a route.
 * Permitted roles can be passed to the factory or declared on the route's
 * `data.roles`; the effective set is their union. An absent or unrecognized
 * role is never permitted and is redirected to the unauthorized fallback.
 *
 * ```ts
 * { path: 'admin', canActivate: [authGuard, roleGuard(Role.Admin)], ... }
 * { path: 'x', canActivate: [authGuard, roleGuard()], data: { roles: [Role.User_Full] } }
 * ```
 */
export function roleGuard(...allowedRoles: Role[]): CanActivateFn {
  return (route) => {
    const tokens = inject(TokenService);
    const router = inject(Router);

    const permitted = resolvePermittedRoles(allowedRoles, route.data?.['roles']);
    const currentRole = tokens.role();

    if (currentRole !== null && permitted.includes(currentRole)) {
      return true;
    }

    // Role missing or not permitted: block and redirect to the fallback.
    return router.createUrlTree([UNAUTHORIZED_FALLBACK]);
  };
}

/** Union of the factory roles and any valid `data.roles` array on the route. */
function resolvePermittedRoles(factoryRoles: Role[], dataRoles: unknown): Role[] {
  const fromData = Array.isArray(dataRoles) ? (dataRoles as Role[]) : [];
  return [...factoryRoles, ...fromData];
}
