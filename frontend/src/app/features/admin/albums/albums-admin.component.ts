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

import { AlbumDto, AlbumWriteDto } from '../../../core/models/album.model';
import { NotificationService } from '../../../core/services/notification.service';
import { AlbumAdminService } from '../shared/album-admin.service';
import { toProblemDetails } from '../shared/http-error';
import { validateCoverFile } from '../shared/image-upload';
import { ADMIN_PAGE_SIZE } from '../shared/paged-result.model';

/**
 * Albums CRUD table and form. Reactive validators mirror the backend contract.
 * A cover image is validated client-side and uploaded only after the record is
 * saved (the upload endpoint needs the record's id). Failures keep the table and
 * the entered form data intact.
 */
@Component({
  selector: 'app-albums-admin',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  templateUrl: './albums-admin.component.html',
  styleUrl: '../admin.component.css',
})
export class AlbumsAdminComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(AlbumAdminService);
  private readonly notifications = inject(NotificationService);

  readonly items: WritableSignal<readonly AlbumDto[]> = signal([]);

  /** 1-based current page number. */
  readonly page = signal(1);

  readonly totalPages = signal(1);

  readonly loading = signal(false);

  /** The id being edited, or `null` when creating a new Album. */
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
    band: ['', [Validators.maxLength(200)]],
    releaseYear: [2000, [Validators.required, Validators.min(1888), Validators.max(2100)]],
    genre: ['', [Validators.maxLength(200)]],
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
  isInvalid(controlName: 'title' | 'band' | 'releaseYear' | 'genre'): boolean {
    const control = this.form.controls[controlName];
    return control.invalid && (control.touched || control.dirty);
  }

  /** Begin editing an existing Album, populating the form. */
  edit(album: AlbumDto): void {
    this.editingId.set(album.id);
    this.clearFile();
    this.form.patchValue({
      title: album.title,
      band: album.band ?? '',
      releaseYear: album.releaseYear,
      genre: album.genre ?? '',
    });
  }

  /** Reset the form to create mode and clear any selected file. */
  resetForm(): void {
    this.editingId.set(null);
    this.clearFile();
    this.form.reset({ title: '', band: '', releaseYear: 2000, genre: '' });
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
   * Save the form. The cover is uploaded only after the Album is saved, since the
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

    const save$: Observable<AlbumDto> = id
      ? this.service.update(id, dto)
      : this.service.create(dto);

    save$
      .pipe(
        switchMap((album) =>
          file ? this.service.uploadCover(album.id, file).pipe(switchMap(() => of(album))) : of(album),
        ),
      )
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.notifications.success(id ? 'Album updated.' : 'Album created.');
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

  /** Delete an Album, reloading the current page on success. */
  remove(album: AlbumDto): void {
    if (this.saving()) {
      return;
    }
    this.saving.set(true);
    this.service.delete(album.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.notifications.success('Album deleted.');
        if (this.editingId() === album.id) {
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
  private toDto(): AlbumWriteDto {
    const value = this.form.getRawValue();
    const band = value.band.trim();
    const genre = value.genre.trim();
    return {
      title: value.title,
      band: band.length > 0 ? band : null,
      releaseYear: value.releaseYear,
      genre: genre.length > 0 ? genre : null,
    };
  }

  private clearFile(): void {
    this.selectedFile.set(null);
    this.coverError.set(null);
  }
}
