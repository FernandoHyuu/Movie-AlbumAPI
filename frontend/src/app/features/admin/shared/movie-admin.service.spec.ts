import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../../environments/environment';
import { MovieDto, MovieWriteDto } from '../../../core/models/movie.model';
import { MovieAdminService } from './movie-admin.service';
import { ADMIN_PAGE_SIZE, PagedResult } from './paged-result.model';

const MOVIES_BASE = `${environment.apiBaseUrl}/movies`;

function makeMovie(overrides: Partial<MovieDto> = {}): MovieDto {
  return {
    id: 'm1',
    title: 'The Matrix',
    studio: 'Warner Bros.',
    releaseYear: 1999,
    mainActors: ['Keanu Reeves'],
    hasCover: false,
    ...overrides,
  };
}

function makeWriteDto(overrides: Partial<MovieWriteDto> = {}): MovieWriteDto {
  return {
    title: 'The Matrix',
    studio: 'Warner Bros.',
    releaseYear: 1999,
    mainActors: ['Keanu Reeves'],
    ...overrides,
  };
}

describe('MovieAdminService', () => {
  let service: MovieAdminService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(MovieAdminService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('requests a page capped at the admin page size (R20.1)', () => {
    const page: PagedResult<MovieDto> = {
      items: [makeMovie()],
      pageNumber: 1,
      pageSize: ADMIN_PAGE_SIZE,
      totalCount: 1,
      totalPages: 1,
    };

    service.list(1).subscribe((result) => expect(result).toEqual(page));

    const req = httpMock.expectOne(
      (r) => r.url === MOVIES_BASE && r.params.get('pageSize') === String(ADMIN_PAGE_SIZE),
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('1');
    req.flush(page);
  });

  it('posts the write DTO to create a movie (R20.3)', () => {
    const dto = makeWriteDto();
    const created = makeMovie({ id: 'new-id' });

    service.create(dto).subscribe((result) => expect(result).toEqual(created));

    const req = httpMock.expectOne(MOVIES_BASE);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(dto);
    req.flush(created);
  });

  it('puts the write DTO to update a movie (R20.3)', () => {
    const dto = makeWriteDto({ title: 'Updated' });

    service.update('m1', dto).subscribe((result) => expect(result.id).toBe('m1'));

    const req = httpMock.expectOne(`${MOVIES_BASE}/m1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(dto);
    req.flush(makeMovie({ id: 'm1', title: 'Updated' }));
  });

  it('issues a DELETE to remove a movie (R20.7)', () => {
    service.delete('m1').subscribe((result) => expect(result).toBeNull());

    const req = httpMock.expectOne(`${MOVIES_BASE}/m1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('uploads a cover as multipart form data under the file field (R20.5)', () => {
    const file = new File([new Uint8Array(8)], 'cover.jpg', { type: 'image/jpeg' });

    service.uploadCover('m1', file).subscribe();

    const req = httpMock.expectOne(`${MOVIES_BASE}/m1/cover`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    expect((req.request.body as FormData).get('file')).toBeTruthy();
    req.flush(null);
  });

  it('propagates a server error on create failure (R20.8)', () => {
    let errorStatus = 0;

    service.create(makeWriteDto()).subscribe({
      next: () => fail('expected an error'),
      error: (err) => (errorStatus = err.status),
    });

    httpMock
      .expectOne(MOVIES_BASE)
      .flush({ detail: 'Server error' }, { status: 500, statusText: 'Server Error' });

    expect(errorStatus).toBe(500);
  });
});
