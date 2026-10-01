import { Routes } from '@angular/router';

import { Role } from '../../core/models/role.enum';
import { authGuard } from '../../core/guards/auth.guard';
import { roleGuard } from '../../core/guards/role.guard';
import { AdminComponent } from './admin.component';

/**
 * Admin panel, mounted under `/admin` behind the auth and Admin role guards.
 * The component also re-checks the Admin role at render time as defense in depth.
 */
export const adminRoutes: Routes = [
  {
    path: '',
    component: AdminComponent,
    canActivate: [authGuard, roleGuard(Role.Admin)],
  },
];
