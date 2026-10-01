import {
  ChangeDetectionStrategy,
  Component,
  WritableSignal,
  computed,
  inject,
  signal,
} from '@angular/core';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Observable, of, switchMap } from 'rxjs';

import { MovieDto, MovieWriteDto } from '../../../core/models/movie.model';
import { NotificationService } from '../../../core/services/notification.service';
import { toProblemDetails } from '../shared/http-error';
import { validateCoverFile } from '../shared/image-upload';
import { MovieAdminService } from '../shared/movie-admin.service';
import { ADMIN_PAGE_SIZE } from '../shared/paged-result.model';

/**
 * Movies CRUD table and form. Reactive validators mirror the backend contract.
 * A cover image is validated client-side and uploaded only after the record is
 * saved (the upload endpoint needs the record's id). Failures keep the table and
 * the entered form data intact.
 */
@Component({
  selector: 'app-movies-admin',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  templateUrl: './movies-admin.component.html',
  styleUrl: '../admin.component.css',
})
export class MoviesAdminComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(MovieAdminService);
  private readonly notifications = inject(NotificationService);

  readonly items: WritableSignal<readonly MovieDto[]> = signal([]);

  /** 1-based current page number. */
  readonly page = signal(1);

  readonly totalPages = signal(1);

  readonly loading = signal(false);

  /** The id being edited, or `null` when creating a new Movie. */
  readonly editingId: WritableSignal<string | null> = signal(null);

  /** `true` while a create/update/delete request is in flight. */
  readonly saving = signal(false);

  readonly isEditing = computed(() => this.editingId() !== null);

  /** The selected cover file pending upload, or `null`. */
  readonly selectedFile: WritableSignal<File | null> = signal(null);

  /** Client-side cover validation error, or `null` when the file is acceptable. */
  readonly coverError: WritableSignal<string | null> = signal(null);

  readonly form = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    studio: ['', [Validators.maxLength(200)]],
    releaseYear: [2000, [Validators.required, Validators.min(1888), Validators.max(2100)]],
    mainActors: ['', [Validators.maxLength(10000)]],
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.service.list(this.page(), ADMIN_PAGE_SIZE).subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.totalPages.set(Math.max(1, result.totalPages));
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.notifications.error(toProblemDetails(error));
      },
    });
  }

  /** Go to a specific 1-based page, clamped to the available range. */
  goToPage(page: number): void {
    const clamped = Math.min(Math.max(1, page), this.totalPages());
    if (clamped !== this.page()) {
      this.page.set(clamped);
      this.load();
    }
  }

  /** `true` when a control is invalid and has been touched or changed. */
  isInvalid(controlName: 'title' | 'studio' | 'releaseYear' | 'mainActors'): boolean {
    const control = this.form.controls[controlName];
    return control.invalid && (control.touched || control.dirty);
  }

  /** Begin editing an existing Movie, populating the form. */
  edit(movie: MovieDto): void {
    this.editingId.set(movie.id);
    this.clearFile();
    this.form.patchValue({
      title: movie.title,
      studio: movie.studio ?? '',
      releaseYear: movie.releaseYear,
      mainActors: movie.mainActors.join(', '),
    });
  }

  /** Reset the form to create mode and clear any selected file. */
  resetForm(): void {
    this.editingId.set(null);
    this.clearFile();
    this.form.reset({ title: '', studio: '', releaseYear: 2000, mainActors: '' });
  }

  /** Validate a selected cover file client-side; an invalid file is rejected and not retained. */
  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    if (!file) {
      this.clearFile();
      return;
    }
    const result = validateCoverFile(file);
    if (!result.valid) {
      this.selectedFile.set(null);
      this.coverError.set(result.message ?? 'Invalid image.');
      input.value = '';
      return;
    }
    this.coverError.set(null);
    this.selectedFile.set(file);
  }

  /**
   * Save the form. The cover is uploaded only after the Movie is saved, since the
   * upload endpoint needs the new record's id. On failure the form data is kept.
   */
  submit(): void {
    if (this.form.invalid || this.coverError() !== null || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    const dto = this.toDto();
    const id = this.editingId();
    const file = this.selectedFile();
    this.saving.set(true);

    const save$: Observable<MovieDto> = id
      ? this.service.update(id, dto)
      : this.service.create(dto);

    save$
      .pipe(
        switchMap((movie) =>
          file ? this.service.uploadCover(movie.id, file).pipe(switchMap(() => of(movie))) : of(movie),
        ),
      )
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.notifications.success(id ? 'Movie updated.' : 'Movie created.');
          this.resetForm();
          this.load();
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.notifications.error(toProblemDetails(error));
          // Form data is left intact so the user can correct and resubmit.
        },
      });
  }

  /** Delete a Movie, reloading the current page on success. */
  remove(movie: MovieDto): void {
    if (this.saving()) {
      return;
    }
    this.saving.set(true);
    this.service.delete(movie.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.notifications.success('Movie deleted.');
        if (this.editingId() === movie.id) {
          this.resetForm();
        }
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.notifications.error(toProblemDetails(error));
      },
    });
  }

  /** Build the write DTO from the current form value. */
  private toDto(): MovieWriteDto {
    const value = this.form.getRawValue();
    const studio = value.studio.trim();
    const mainActors = value.mainActors
      .split(',')
      .map((actor) => actor.trim())
      .filter((actor) => actor.length > 0);
    return {
      title: value.title,
      studio: studio.length > 0 ? studio : null,
      releaseYear: value.releaseYear,
      mainActors,
    };
  }

  private clearFile(): void {
    this.selectedFile.set(null);
    this.coverError.set(null);
  }
}
