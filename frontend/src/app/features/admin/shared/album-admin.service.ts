import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../../environments/environment';
import { AlbumDto, AlbumWriteDto } from '../../../core/models/album.model';
import { buildCoverFormData } from './image-upload';
import { ADMIN_PAGE_SIZE, PagedResult } from './paged-result.model';

/**
 * Data access for Album management in the admin panel. Wraps `/api/albums` CRUD
 * plus the `{id}/cover` upload endpoint; covers go as `multipart/form-data`.
 */
@Injectable({ providedIn: 'root' })
export class AlbumAdminService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/albums`;

  /** Fetch one page of Albums (1-based `page`). */
  list(page: number, pageSize: number = ADMIN_PAGE_SIZE): Observable<PagedResult<AlbumDto>> {
    const params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    return this.http.get<PagedResult<AlbumDto>>(this.baseUrl, { params });
  }

  create(dto: AlbumWriteDto): Observable<AlbumDto> {
    return this.http.post<AlbumDto>(this.baseUrl, dto);
  }

  update(id: string, dto: AlbumWriteDto): Observable<AlbumDto> {
    return this.http.put<AlbumDto>(`${this.baseUrl}/${id}`, dto);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  /** Upload a cover image for the Album as multipart form data. */
  uploadCover(id: string, file: File): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/cover`, buildCoverFormData(file));
  }
}
