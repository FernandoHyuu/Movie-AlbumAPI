import { Routes } from '@angular/router';

import { authGuard } from './core/guards/auth.guard';
import { roleGuard } from './core/guards/role.guard';
import { Role } from './core/models/role.enum';

import { authRoutes } from './features/auth/auth.routes';
import { moviesRoutes } from './features/movies/movies.routes';
import { albumsRoutes } from './features/albums/albums.routes';
import { adminRoutes } from './features/admin/admin.routes';

import { LayoutShellComponent } from './shared/components/layout-shell/layout-shell.component';
import { UnauthorizedComponent } from './shared/components/unauthorized/unauthorized.component';

export const routes: Routes = [
  // Public auth routes (e.g. /login), reachable without a session.
  ...authRoutes,

  // Fallback the role guard redirects to when a role is not permitted.
  { path: 'unauthorized', component: UnauthorizedComponent },

  // Protected shell: sidebar + spinner + toast chrome around the routed feature
  // views, all behind the auth guard. Each catalog is further gated by role.
  {
    path: '',
    component: LayoutShellComponent,
    canActivate: [authGuard],
    children: [
      {
        path: 'movies',
        canActivate: [roleGuard(Role.User_Movie, Role.User_Full, Role.Admin)],
        children: moviesRoutes,
      },
      {
        path: 'albums',
        canActivate: [roleGuard(Role.User_Album, Role.User_Full, Role.Admin)],
        children: albumsRoutes,
      },
      {
        path: 'admin',
        children: adminRoutes,
      },
      // Landing route inside the shell.
      { path: '', pathMatch: 'full', redirectTo: 'movies' },
    ],
  },

  // Unknown paths fall back to the catalog (auth guard bounces to /login if needed).
  { path: '**', redirectTo: 'movies' },
];
