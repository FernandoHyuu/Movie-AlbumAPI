import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../../environments/environment';
import { PersonDto, PersonWriteDto } from '../../../core/models/person.model';
import { ADMIN_PAGE_SIZE, PagedResult } from './paged-result.model';

/** Data access for Person management in the admin panel. Wraps `/api/persons` CRUD. */
@Injectable({ providedIn: 'root' })
export class PersonAdminService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/persons`;

  /** Fetch one page of Persons (1-based `page`). */
  list(page: number, pageSize: number = ADMIN_PAGE_SIZE): Observable<PagedResult<PersonDto>> {
    const params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    return this.http.get<PagedResult<PersonDto>>(this.baseUrl, { params });
  }

  create(dto: PersonWriteDto): Observable<PersonDto> {
    return this.http.post<PersonDto>(this.baseUrl, dto);
  }

  update(id: string, dto: PersonWriteDto): Observable<PersonDto> {
    return this.http.put<PersonDto>(`${this.baseUrl}/${id}`, dto);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
