import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import fc from 'fast-check';
import { Role } from '../../../core/models/role.enum';
import { AuthService } from '../../../core/services/auth.service';
import { TokenService } from '../../../core/services/token.service';
import { SidebarComponent } from './sidebar.component';

/**
 * The menus the permission matrix (R16.1–R16.4) expects for a given role, and an
 * empty list for the `null` (unauthorized) role (R16.6). This is an independent
 * restatement of the matrix so the test fails if the component's mapping drifts.
 */
const EXPECTED_LABELS: Record<Role, readonly string[]> = {
  [Role.User_Movie]: ['Movies'],
  [Role.User_Album]: ['Albums'],
  [Role.User_Full]: ['Movies', 'Albums'],
  [Role.Admin]: ['Movies', 'Albums', 'Management'],
};

/**
 * A TokenService stub whose `role()` reads from a writable signal the test sets
 * before each render, driving the component's derived `menuItems()` (R16.5).
 * `hasAccessToken()` reads a separate writable signal that drives the Logout
 * control's visibility.
 */
class TokenServiceStub {
  readonly roleSignal: WritableSignal<Role | null> = signal<Role | null>(null);
  readonly role = this.roleSignal.asReadonly();
  readonly hasAccessTokenSignal: WritableSignal<boolean> = signal<boolean>(false);
  readonly hasAccessToken = this.hasAccessTokenSignal.asReadonly();
}

/** An AuthService stub with a spyable `logout` for asserting the sign-out flow. */
class AuthServiceStub {
  logout = jasmine.createSpy('logout');
}

/** Generate every recognized role plus `null` (absent/expired/unrecognized → R16.6). */
const roleArb: fc.Arbitrary<Role | null> = fc.constantFrom(
  Role.User_Movie,
  Role.User_Album,
  Role.User_Full,
  Role.Admin,
  null,
);

describe('SidebarComponent', () => {
  let tokenStub: TokenServiceStub;
  let authStub: AuthServiceStub;

  beforeEach(() => {
    tokenStub = new TokenServiceStub();
    authStub = new AuthServiceStub();
    TestBed.configureTestingModule({
      imports: [SidebarComponent],
      providers: [
        provideRouter([]),
        { provide: TokenService, useValue: tokenStub },
        { provide: AuthService, useValue: authStub },
      ],
    });
  });

  /** Render the sidebar fresh for the given role and return its fixture. */
  function renderFor(role: Role | null): ComponentFixture<SidebarComponent> {
    tokenStub.roleSignal.set(role);
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();
    return fixture;
  }

  /** The visible labels of the rendered catalog/management anchors, in DOM order. */
  function renderedLabels(fixture: ComponentFixture<SidebarComponent>): string[] {
    const host = fixture.nativeElement as HTMLElement;
    const anchors = host.querySelectorAll<HTMLAnchorElement>('a.sidebar__link');
    return Array.from(anchors, (a) => a.textContent?.trim() ?? '');
  }

  it('Property 31: renders exactly the menus permitted by the role', () => {
    fc.assert(
      fc.property(roleArb, (role) => {
        const fixture = renderFor(role);
        try {
          const expected = role !== null ? EXPECTED_LABELS[role] : [];

          // The derived signal matches the matrix (R16.1–R16.5).
          expect(fixture.componentInstance.menuItems().map((m) => m.label)).toEqual(
            expected as string[],
          );

          // The rendered DOM shows exactly those menus and no others.
          expect(renderedLabels(fixture)).toEqual(expected as string[]);

          const host = fixture.nativeElement as HTMLElement;
          const unauthorized = host.querySelector('.sidebar__unauthorized');
          if (role === null) {
            // No catalog/management menus render and the unauthorized indicator shows (R16.6).
            expect(renderedLabels(fixture)).toEqual([]);
            expect(unauthorized).not.toBeNull();
          } else {
            // A recognized role shows menus and no unauthorized indicator.
            expect(unauthorized).toBeNull();
          }
        } finally {
          fixture.destroy();
        }
      }),
      { numRuns: 100 },
    );
  });

  it('shows the Logout button only when a session exists', () => {
    tokenStub.hasAccessTokenSignal.set(false);
    const anon = TestBed.createComponent(SidebarComponent);
    anon.detectChanges();
    expect(
      (anon.nativeElement as HTMLElement).querySelector('.sidebar__logout'),
    ).toBeNull();
    anon.destroy();

    tokenStub.hasAccessTokenSignal.set(true);
    const authed = TestBed.createComponent(SidebarComponent);
    authed.detectChanges();
    expect(
      (authed.nativeElement as HTMLElement).querySelector('.sidebar__logout'),
    ).not.toBeNull();
    authed.destroy();
  });

  it('logs out and navigates to /login when Logout is clicked', () => {
    tokenStub.hasAccessTokenSignal.set(true);
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();

    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);

    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '.sidebar__logout',
    );
    expect(button).not.toBeNull();
    button!.click();

    expect(authStub.logout).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledWith(['/login']);
    fixture.destroy();
  });
});
