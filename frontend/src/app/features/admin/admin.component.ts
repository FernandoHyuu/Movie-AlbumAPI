import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';

import { Role } from '../../core/models/role.enum';
import { TokenService } from '../../core/services/token.service';
import { AlbumsAdminComponent } from './albums/albums-admin.component';
import { MoviesAdminComponent } from './movies/movies-admin.component';
import { PersonsAdminComponent } from './persons/persons-admin.component';

/** The management tables the admin panel exposes. */
type AdminTab = 'persons' | 'movies' | 'albums';

/**
 * Admin panel hosting CRUD tables for Persons, Movies, and Albums behind a tab
 * switcher. The route is already guarded, but this component re-checks the Admin
 * role as defense in depth and shows a "privileges required" message otherwise.
 */
@Component({
  selector: 'app-admin',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PersonsAdminComponent, MoviesAdminComponent, AlbumsAdminComponent],
  templateUrl: './admin.component.html',
  styleUrl: './admin.component.css',
})
export class AdminComponent {
  private readonly tokens = inject(TokenService);

  /** `true` when the signed-in user holds the `Admin` role. */
  readonly isAdmin = computed(() => this.tokens.role() === Role.Admin);

  readonly activeTab = signal<AdminTab>('persons');

  select(tab: AdminTab): void {
    this.activeTab.set(tab);
  }
}
