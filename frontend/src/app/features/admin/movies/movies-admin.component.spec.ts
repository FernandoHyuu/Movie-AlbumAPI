import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { MovieDto, MovieWriteDto } from '../../../core/models/movie.model';
import { NotificationService } from '../../../core/services/notification.service';
import { MovieAdminService } from '../shared/movie-admin.service';
import { PagedResult } from '../shared/paged-result.model';
import { MoviesAdminComponent } from './movies-admin.component';

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

function page(items: MovieDto[], totalPages = 1): PagedResult<MovieDto> {
  return { items, pageNumber: 1, pageSize: 50, totalCount: items.length, totalPages };
}

/** Stub of {@link MovieAdminService} the tests program per scenario. */
class MovieServiceStub {
  listCalls = 0;
  listResult: Observable<PagedResult<MovieDto>> = of(page([makeMovie()]));

  createArg: MovieWriteDto | null = null;
  createResult: Observable<MovieDto> = of(makeMovie({ id: 'created' }));

  updateArg: { id: string; dto: MovieWriteDto } | null = null;
  updateResult: Observable<MovieDto> = of(makeMovie());

  deleteArg: string | null = null;
  deleteResult: Observable<void> = of(undefined);

  uploadCalls: { id: string; file: File }[] = [];
  uploadResult: Observable<void> = of(undefined);

  list(): Observable<PagedResult<MovieDto>> {
    this.listCalls++;
    return this.listResult;
  }
  create(dto: MovieWriteDto): Observable<MovieDto> {
    this.createArg = dto;
    return this.createResult;
  }
  update(id: string, dto: MovieWriteDto): Observable<MovieDto> {
    this.updateArg = { id, dto };
    return this.updateResult;
  }
  delete(id: string): Observable<void> {
    this.deleteArg = id;
    return this.deleteResult;
  }
  uploadCover(id: string, file: File): Observable<void> {
    this.uploadCalls.push({ id, file });
    return this.uploadResult;
  }
}

/** Captures the toasts raised during a flow. */
class NotificationStub {
  successCalls: string[] = [];
  errorCalls: unknown[] = [];
  success(message: string): number {
    this.successCalls.push(message);
    return 0;
  }
  error(problem: unknown): number {
    this.errorCalls.push(problem);
    return 0;
  }
}

function fileInputEvent(file: File | null): Event {
  const input = document.createElement('input');
  input.type = 'file';
  // DataTransfer is available in headless Chrome; fall back to a stub otherwise.
  Object.defineProperty(input, 'files', {
    value: file ? [file] : [],
    configurable: true,
  });
  return { target: input } as unknown as Event;
}

describe('MoviesAdminComponent', () => {
  let service: MovieServiceStub;
  let notifications: NotificationStub;

  function setup(): ComponentFixture<MoviesAdminComponent> {
    service = new MovieServiceStub();
    notifications = new NotificationStub();

    TestBed.configureTestingModule({
      imports: [MoviesAdminComponent],
      providers: [
        { provide: MovieAdminService, useValue: service },
        { provide: NotificationService, useValue: notifications },
      ],
    });

    const fixture = TestBed.createComponent(MoviesAdminComponent);
    fixture.detectChanges(); // triggers the constructor load()
    return fixture;
  }

  function fillValid(fixture: ComponentFixture<MoviesAdminComponent>): void {
    fixture.componentInstance.form.setValue({
      title: 'Inception',
      studio: 'Legendary',
      releaseYear: 2010,
      mainActors: 'Leonardo DiCaprio, Elliot Page',
    });
  }

  it('loads the first page on init (R20.1)', () => {
    const fixture = setup();

    expect(service.listCalls).toBe(1);
    expect(fixture.componentInstance.items().length).toBe(1);
  });

  it('creates a movie, shows a success toast and refreshes the table (R20.3)', () => {
    const fixture = setup();
    fillValid(fixture);

    fixture.componentInstance.submit();

    expect(service.createArg).toEqual({
      title: 'Inception',
      studio: 'Legendary',
      releaseYear: 2010,
      mainActors: ['Leonardo DiCaprio', 'Elliot Page'],
    });
    expect(notifications.successCalls).toEqual(['Movie created.']);
    // Initial load + reload after success.
    expect(service.listCalls).toBe(2);
    expect(fixture.componentInstance.form.getRawValue().title).toBe('');
  });

  it('updates an existing movie and shows a success toast (R20.3)', () => {
    const fixture = setup();
    fixture.componentInstance.edit(makeMovie({ id: 'm1', title: 'Old' }));
    fixture.componentInstance.form.patchValue({ title: 'New Title' });

    fixture.componentInstance.submit();

    expect(service.updateArg?.id).toBe('m1');
    expect(service.updateArg?.dto.title).toBe('New Title');
    expect(notifications.successCalls).toEqual(['Movie updated.']);
    expect(service.listCalls).toBe(2);
  });

  it('deletes a movie and reloads the current page on success (R20.7)', () => {
    const fixture = setup();

    fixture.componentInstance.remove(makeMovie({ id: 'm1' }));

    expect(service.deleteArg).toBe('m1');
    expect(notifications.successCalls).toEqual(['Movie deleted.']);
    // Initial load + reload after delete success.
    expect(service.listCalls).toBe(2);
  });

  it('preserves the table and shows an error toast when create fails (R20.8, R20.4)', () => {
    const fixture = setup();
    fillValid(fixture);
    service.createResult = throwError(
      () => new HttpErrorResponse({ status: 500, error: { detail: 'boom' } }),
    );

    fixture.componentInstance.submit();

    expect(notifications.errorCalls.length).toBe(1);
    expect(notifications.errorCalls[0]).toEqual({ detail: 'boom' });
    // No reload: table state preserved (only the initial load ran).
    expect(service.listCalls).toBe(1);
    expect(fixture.componentInstance.items().length).toBe(1);
    // Form data retained for correction.
    expect(fixture.componentInstance.form.getRawValue().title).toBe('Inception');
    expect(fixture.componentInstance.saving()).toBe(false);
  });

  it('preserves the table and shows an error toast when delete fails (R20.8)', () => {
    const fixture = setup();
    service.deleteResult = throwError(
      () => new HttpErrorResponse({ status: 409, error: { detail: 'conflict' } }),
    );

    fixture.componentInstance.remove(makeMovie({ id: 'm1' }));

    expect(notifications.errorCalls.length).toBe(1);
    expect(notifications.successCalls.length).toBe(0);
    // Only the initial load ran; the table was not refreshed/wiped.
    expect(service.listCalls).toBe(1);
    expect(fixture.componentInstance.items().length).toBe(1);
  });

  it('does not submit, flags fields, and retains data for an invalid form (R20.4)', () => {
    const fixture = setup();
    // Missing title + out-of-range release year.
    fixture.componentInstance.form.setValue({
      title: '',
      studio: 'Studio',
      releaseYear: 1000,
      mainActors: 'Someone',
    });

    fixture.componentInstance.submit();

    expect(service.createArg).toBeNull();
    expect(service.updateArg).toBeNull();
    expect(service.listCalls).toBe(1); // no reload, no request
    expect(fixture.componentInstance.form.invalid).toBe(true);
    expect(fixture.componentInstance.form.controls.title.invalid).toBe(true);
    expect(fixture.componentInstance.form.controls.releaseYear.invalid).toBe(true);
    // Entered values retained.
    expect(fixture.componentInstance.form.getRawValue().studio).toBe('Studio');
    expect(fixture.componentInstance.form.getRawValue().releaseYear).toBe(1000);
  });

  it('rejects an oversized cover client-side without uploading (R20.6)', () => {
    const fixture = setup();
    const bigFile = new File([new Uint8Array(1)], 'big.jpg', { type: 'image/jpeg' });
    Object.defineProperty(bigFile, 'size', { value: 6 * 1024 * 1024 });

    fixture.componentInstance.onFileSelected(fileInputEvent(bigFile));

    expect(fixture.componentInstance.coverError()).toBeTruthy();
    expect(fixture.componentInstance.selectedFile()).toBeNull();

    // Submitting is blocked while a cover error is present; no cover upload occurs.
    fillValid(fixture);
    fixture.componentInstance.submit();
    expect(service.createArg).toBeNull();
    expect(service.uploadCalls.length).toBe(0);
  });

  it('rejects a non-JPEG/PNG cover client-side (R20.6)', () => {
    const fixture = setup();
    const gif = new File([new Uint8Array(8)], 'anim.gif', { type: 'image/gif' });

    fixture.componentInstance.onFileSelected(fileInputEvent(gif));

    expect(fixture.componentInstance.coverError()).toBeTruthy();
    expect(fixture.componentInstance.selectedFile()).toBeNull();
    expect(service.uploadCalls.length).toBe(0);
  });

  it('accepts a valid cover and uploads it after a successful create (R20.5, R20.6)', () => {
    const fixture = setup();
    const good = new File([new Uint8Array(16)], 'poster.png', { type: 'image/png' });

    fixture.componentInstance.onFileSelected(fileInputEvent(good));
    expect(fixture.componentInstance.coverError()).toBeNull();
    expect(fixture.componentInstance.selectedFile()).toBe(good);

    fillValid(fixture);
    fixture.componentInstance.submit();

    expect(service.createArg).not.toBeNull();
    expect(service.uploadCalls.length).toBe(1);
    expect(service.uploadCalls[0].file).toBe(good);
    expect(notifications.successCalls).toEqual(['Movie created.']);
  });
});
