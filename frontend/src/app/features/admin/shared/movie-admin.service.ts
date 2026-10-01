import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../../environments/environment';
import { MovieDto, MovieWriteDto } from '../../../core/models/movie.model';
import { buildCoverFormData } from './image-upload';
import { ADMIN_PAGE_SIZE, PagedResult } from './paged-result.model';

/**
 * Data access for Movie management in the admin panel. Wraps `/api/movies` CRUD
 * plus the `{id}/cover` upload endpoint; covers go as `multipart/form-data`.
 */
@Injectable({ providedIn: 'root' })
export class MovieAdminService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/movies`;

  /** Fetch one page of Movies (1-based `page`). */
  list(page: number, pageSize: number = ADMIN_PAGE_SIZE): Observable<PagedResult<MovieDto>> {
    const params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    return this.http.get<PagedResult<MovieDto>>(this.baseUrl, { params });
  }

  create(dto: MovieWriteDto): Observable<MovieDto> {
    return this.http.post<MovieDto>(this.baseUrl, dto);
  }

  update(id: string, dto: MovieWriteDto): Observable<MovieDto> {
    return this.http.put<MovieDto>(`${this.baseUrl}/${id}`, dto);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  /** Upload a cover image for the Movie as multipart form data. */
  uploadCover(id: string, file: File): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/cover`, buildCoverFormData(file));
  }
}
