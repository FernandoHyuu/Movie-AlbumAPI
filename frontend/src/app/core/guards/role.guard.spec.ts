import { Signal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import * as fc from 'fast-check';

import { Role } from '../models/role.enum';
import { TokenService } from '../services/token.service';
import { roleGuard } from './role.guard';

/**
 * Property test for the functional {@link roleGuard} (task 9.5).
 *
 * - Property 30: for any current role and any permitted-role set, the guard
 *   permits navigation (returns `true`) if and only if the current role is
 *   non-null AND a member of the permitted set; otherwise it blocks the route
 *   and redirects to the unauthorized fallback (returns a `UrlTree`), exposing
 *   none of the route content (R15.2, R15.6). The `/admin` case verifies that
 *   `roleGuard(Role.Admin)` permits exactly `Admin` and redirects every other
 *   role and the absent role (R15.3, R15.4).
 *
 * The guard reads `TokenService.role()` and `route.data.roles`, and calls
 * `inject(Router).createUrlTree` on denial, so each run is executed inside a
 * TestBed injection context with a real Router (so `createUrlTree` yields a
 * genuine `UrlTree`) and a TokenService stub whose `role()` signal returns the
 * generated role.
 */

const RUNS = 100;

/** The four defined roles plus `null` (absent/unrecognized claim). */
const ALL_ROLES: readonly Role[] = Object.values(Role);
const currentRoleArb: fc.Arbitrary<Role | null> = fc.constantFrom<(Role | null)[]>(
  ...ALL_ROLES,
  null,
);
/** An arbitrary subset of the defined roles (the route's permitted set). */
const permittedSetArb: fc.Arbitrary<Role[]> = fc.subarray([...ALL_ROLES]);

/** TokenService stub exposing a settable `role()` signal; other members unused here. */
class TokenServiceStub {
  readonly roleSignal = signal<Role | null>(null);
  readonly role: Signal<Role | null> = this.roleSignal.asReadonly();
}

/** Configure a fresh TestBed with a real Router and the TokenService stub. */
function setup(): TokenServiceStub {
  const tokens = new TokenServiceStub();
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: TokenService, useValue: tokens }],
  });
  return tokens;
}

/** Minimal route snapshot carrying only the `data.roles` the guard inspects. */
function routeWithRoles(roles?: Role[]): ActivatedRouteSnapshot {
  return { data: roles === undefined ? {} : { roles } } as ActivatedRouteSnapshot;
}

/** Execute a CanActivateFn inside the injection context, returning its result. */
function runGuard(guard: CanActivateFn, route: ActivatedRouteSnapshot): boolean | UrlTree {
  const state = { url: '/target' } as RouterStateSnapshot;
  return TestBed.runInInjectionContext(() => guard(route, state)) as boolean | UrlTree;
}

describe('roleGuard property test', () => {
  it('Property 30: permits iff the current role is in the permitted set, else redirects (R15.2, R15.6)', () => {
    // Validates: Requirements 15.2, 15.6
    fc.assert(
      fc.property(currentRoleArb, permittedSetArb, (currentRole, permitted) => {
        const tokens = setup();
        tokens.roleSignal.set(currentRole);

        const guard = roleGuard(...permitted);
        const result = runGuard(guard, routeWithRoles());

        const shouldPermit = currentRole !== null && permitted.includes(currentRole);
        if (shouldPermit) {
          // Permit: exactly `true`, never a redirect.
          expect(result).toBe(true);
        } else {
          // Deny: a redirect UrlTree, never `true` (exposes none of the route).
          expect(result).not.toBe(true);
          expect(result instanceof UrlTree).toBe(true);
        }
      }),
      { numRuns: RUNS },
    );
  });

  it('Property 30: route `data.roles` is honored identically to factory args (R15.2, R15.6)', () => {
    // Validates: Requirements 15.2, 15.6
    fc.assert(
      fc.property(currentRoleArb, permittedSetArb, (currentRole, permitted) => {
        const tokens = setup();
        tokens.roleSignal.set(currentRole);

        // Permitted set supplied via route data instead of factory arguments.
        const guard = roleGuard();
        const result = runGuard(guard, routeWithRoles(permitted));

        const shouldPermit = currentRole !== null && permitted.includes(currentRole);
        if (shouldPermit) {
          expect(result).toBe(true);
        } else {
          expect(result).not.toBe(true);
          expect(result instanceof UrlTree).toBe(true);
        }
      }),
      { numRuns: RUNS },
    );
  });

  it('Property 30: roleGuard(Admin) permits exactly Admin and redirects everything else (R15.3, R15.4)', () => {
    // Validates: Requirements 15.3, 15.4
    fc.assert(
      fc.property(currentRoleArb, (currentRole) => {
        const tokens = setup();
        tokens.roleSignal.set(currentRole);

        const guard = roleGuard(Role.Admin);
        const result = runGuard(guard, routeWithRoles());

        if (currentRole === Role.Admin) {
          // Admin reaches /admin (R15.4).
          expect(result).toBe(true);
        } else {
          // Every non-Admin role and the absent role is redirected (R15.3).
          expect(result).not.toBe(true);
          expect(result instanceof UrlTree).toBe(true);
        }
      }),
      { numRuns: RUNS },
    );
  });

  it('redirects to the /unauthorized fallback on denial (R15.2)', () => {
    const tokens = setup();
    tokens.roleSignal.set(Role.User_Movie);

    const guard = roleGuard(Role.Admin);
    const result = runGuard(guard, routeWithRoles());

    expect(result instanceof UrlTree).toBe(true);
    const router = TestBed.inject(Router);
    expect(router.serializeUrl(result as UrlTree)).toBe('/unauthorized');
  });
});
