import { Component, Signal, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { Role } from '../../../core/models/role.enum';
import { AuthService } from '../../../core/services/auth.service';
import { TokenService } from '../../../core/services/token.service';

/** A single navigation entry rendered in the sidebar. */
export interface SidebarMenuItem {
  /** Visible menu label. */
  readonly label: string;
  /** Router path the menu navigates to. */
  readonly route: string;
}

/** The Movies catalog menu entry. */
const MOVIES_MENU: SidebarMenuItem = { label: 'Movies', route: '/movies' };
/** The Albums catalog menu entry. */
const ALBUMS_MENU: SidebarMenuItem = { label: 'Albums', route: '/albums' };
/** The administrative management panel entry. */
const MANAGEMENT_MENU: SidebarMenuItem = { label: 'Management', route: '/admin' };

/**
 * Menus visible to each role: `User_Movie` → Movies; `User_Album` → Albums;
 * `User_Full` → both; `Admin` → both plus management.
 */
const MENUS_BY_ROLE: Record<Role, readonly SidebarMenuItem[]> = {
  [Role.User_Movie]: [MOVIES_MENU],
  [Role.User_Album]: [ALBUMS_MENU],
  [Role.User_Full]: [MOVIES_MENU, ALBUMS_MENU],
  [Role.Admin]: [MOVIES_MENU, ALBUMS_MENU, MANAGEMENT_MENU],
};

/**
 * Role-aware navigation sidebar. The visible menus are derived from
 * {@link TokenService.role}, so navigation tracks the decoded role claim. A
 * `null` role renders no catalog menus and shows an unauthorized indicator.
 */
@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="sidebar" aria-label="Main navigation">
      @if (menuItems().length > 0) {
        <ul class="sidebar__menu">
          @for (item of menuItems(); track item.route) {
            <li class="sidebar__item">
              <a
                [routerLink]="item.route"
                routerLinkActive="sidebar__link--active"
                class="sidebar__link"
                >{{ item.label }}</a
              >
            </li>
          }
        </ul>
      } @else {
        <p class="sidebar__unauthorized" role="alert">
          You are not authorized to view any menus. Please sign in.
        </p>
      }

      @if (isAuthenticated()) {
        <button type="button" class="sidebar__logout" (click)="logout()">
          Logout
        </button>
      }
    </nav>
  `,
  styleUrl: './sidebar.component.css',
})
export class SidebarComponent {
  private readonly tokenService = inject(TokenService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Menus for the current role, or an empty list (unauthorized) when the role is `null`. */
  readonly menuItems: Signal<readonly SidebarMenuItem[]> = computed(() => {
    const role = this.tokenService.role();
    return role !== null ? MENUS_BY_ROLE[role] : [];
  });

  /**
   * Whether a session is stored. Drives the Logout control, which shows whenever
   * a token exists — even with an unrecognized role, which still counts as signed in.
   */
  readonly isAuthenticated: Signal<boolean> = this.tokenService.hasAccessToken;

  /** Sign the current user out and return to the login screen. */
  logout(): void {
    this.auth.logout();
    void this.router.navigate(['/login']);
  }
}
