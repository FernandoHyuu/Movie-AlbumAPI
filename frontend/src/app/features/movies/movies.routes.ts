import { Routes } from '@angular/router';

import { MoviesComponent } from './movies.component';

/** Movie catalog view, mounted under `/movies` behind the auth and role guards. */
export const moviesRoutes: Routes = [
  { path: '', component: MoviesComponent },
];
