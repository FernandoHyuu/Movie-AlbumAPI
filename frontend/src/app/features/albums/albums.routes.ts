import { Routes } from '@angular/router';

import { AlbumsComponent } from './albums.component';

/** Album catalog view, mounted under `/albums` behind the auth and role guards. */
export const albumsRoutes: Routes = [
  { path: '', component: AlbumsComponent },
];
