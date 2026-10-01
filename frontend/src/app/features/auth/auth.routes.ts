import { Routes } from '@angular/router';

import { LoginComponent } from './login.component';

/** Public login view at `/login`; the guard redirects unauthenticated users here. */
export const authRoutes: Routes = [
  { path: 'login', component: LoginComponent },
];
