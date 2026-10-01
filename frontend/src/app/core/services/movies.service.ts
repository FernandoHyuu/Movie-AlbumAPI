import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { MovieDto } from '../models/movie.model';
import { PagedResult } from '../models/paged-result.model';

/**
 * Reads the Movie catalog from the API. `coverUrl` is built here (next to the
 * base URL) so callers never hard-code the path. Auth headers and 401 refresh
 * are handled by the HTTP interceptor, keeping this a thin data layer.
 */
@Injectable({ providedIn: 'root' })
export class MoviesService {
  private readonly http = inject(HttpClient);

  private readonly baseUrl = `${environment.apiBaseUrl}/movies`;

  /** Fetch one page of movies (1-based `page`). */
  getPage(page: number, pageSize: number): Observable<PagedResult<MovieDto>> {
    const params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    return this.http.get<PagedResult<MovieDto>>(this.baseUrl, { params });
  }

  /** Cover endpoint URL for a movie, or `null` when it has no stored cover. */
  coverUrl(movie: MovieDto): string | null {
    return movie.hasCover ? `${this.baseUrl}/${movie.id}/cover` : null;
  }
}
