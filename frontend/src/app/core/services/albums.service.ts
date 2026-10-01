import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { AlbumDto } from '../models/album.model';
import { PagedResult } from '../models/paged-result.model';

/**
 * Reads the Album catalog from the API. `coverUrl` is built here (next to the
 * base URL) so callers never hard-code the path. Auth headers and 401 refresh
 * are handled by the HTTP interceptor, keeping this a thin data layer.
 */
@Injectable({ providedIn: 'root' })
export class AlbumsService {
  private readonly http = inject(HttpClient);

  private readonly baseUrl = `${environment.apiBaseUrl}/albums`;

  /** Fetch one page of albums (1-based `page`). */
  getPage(page: number, pageSize: number): Observable<PagedResult<AlbumDto>> {
    const params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    return this.http.get<PagedResult<AlbumDto>>(this.baseUrl, { params });
  }

  /** Cover endpoint URL for an album, or `null` when it has no stored cover. */
  coverUrl(album: AlbumDto): string | null {
    return album.hasCover ? `${this.baseUrl}/${album.id}/cover` : null;
  }
}
