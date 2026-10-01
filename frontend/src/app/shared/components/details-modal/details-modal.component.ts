import {
  Component,
  ElementRef,
  computed,
  inject,
  output,
  signal,
} from '@angular/core';
import { AlbumDto } from '../../../core/models/album.model';
import { MovieDto } from '../../../core/models/movie.model';

/** Discriminates which field set the modal renders. */
export type MediaKind = 'movie' | 'album';

/** A selected media item plus the discriminator for which field set to display. */
export type SelectedMedia =
  | { readonly kind: 'movie'; readonly item: MovieDto }
  | { readonly kind: 'album'; readonly item: AlbumDto };

/** A single label/value pair rendered as a detail row in the modal. */
export interface DetailField {
  /** Human-readable field label. */
  readonly label: string;
  /** Display value for the field. */
  readonly value: string;
}

/**
 * Modal showing the details of a selected Movie or Album. Opening is
 * signal-driven: {@link open} sets the selection and an `@if` renders the
 * dialog. Uses accessible dialog semantics, dismisses on Escape, and on close
 * refocuses the element that opened it (the triggering card) for keyboard users.
 */
@Component({
  selector: 'app-details-modal',
  template: `
    @if (selected(); as sel) {
      <div
        class="details-modal__backdrop"
        (click)="onBackdropClick($event)"
      >
        <div
          #dialog
          class="details-modal"
          role="dialog"
          aria-modal="true"
          [attr.aria-labelledby]="titleId"
          tabindex="-1"
          (keydown.escape)="close()"
        >
          <header class="details-modal__header">
            <h2 [id]="titleId" class="details-modal__title">{{ title() }}</h2>
            <button
              type="button"
              class="details-modal__close"
              aria-label="Close details"
              (click)="close()"
            >
              &times;
            </button>
          </header>

          <dl class="details-modal__fields">
            @for (field of fields(); track field.label) {
              <div class="details-modal__row">
                <dt class="details-modal__label">{{ field.label }}</dt>
                <dd class="details-modal__value">{{ field.value }}</dd>
              </div>
            }
          </dl>
        </div>
      </div>
    }
  `,
  styleUrl: './details-modal.component.css',
})
export class DetailsModalComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** DOM id linking the dialog title to its `aria-labelledby`. */
  readonly titleId = 'details-modal-title';

  /** Emitted after the modal closes, once focus has been returned. */
  readonly closed = output<void>();

  /** The currently displayed selection, or `null` when the modal is closed. */
  private readonly selectedSignal = signal<SelectedMedia | null>(null);

  /** The element to refocus on close — captured when the modal opens. */
  private previouslyFocused: HTMLElement | null = null;

  /** The currently displayed selection, or `null` when the modal is closed. */
  readonly selected = this.selectedSignal.asReadonly();

  /** The dialog title: the selected item's Title. */
  readonly title = computed(() => {
    const sel = this.selectedSignal();
    return sel === null ? '' : sel.item.title;
  });

  /** Ordered detail rows for the current selection, or empty when closed. */
  readonly fields = computed<readonly DetailField[]>(() => {
    const sel = this.selectedSignal();
    if (sel === null) {
      return [];
    }
    return sel.kind === 'movie'
      ? movieFields(sel.item)
      : albumFields(sel.item);
  });

  /**
   * Open the modal for the given selection. Captures the currently-focused
   * element to restore on close, then moves focus into the dialog next microtask.
   */
  open(selection: SelectedMedia): void {
    const active = typeof document !== 'undefined' ? document.activeElement : null;
    this.previouslyFocused =
      active instanceof HTMLElement ? active : null;
    this.selectedSignal.set(selection);
    queueMicrotask(() => this.focusDialog());
  }

  /**
   * Close the modal, return focus to the element that opened it (the triggering
   * card), then emit {@link closed}.
   */
  close(): void {
    if (this.selectedSignal() === null) {
      return;
    }
    this.selectedSignal.set(null);
    const target = this.previouslyFocused;
    this.previouslyFocused = null;
    target?.focus();
    this.closed.emit();
  }

  /** Close when the click lands on the backdrop rather than the dialog. */
  onBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.close();
    }
  }

  /** Move focus into the rendered dialog element so Escape and tab work. */
  private focusDialog(): void {
    const dialog = this.host.nativeElement.querySelector<HTMLElement>(
      '.details-modal',
    );
    dialog?.focus();
  }
}

/** Build the Movie detail rows: Title, Studio, ReleaseYear, MainActors. */
function movieFields(movie: MovieDto): readonly DetailField[] {
  return [
    { label: 'Title', value: movie.title },
    { label: 'Studio', value: movie.studio ?? '—' },
    { label: 'Release Year', value: String(movie.releaseYear) },
    {
      label: 'Main Actors',
      value: movie.mainActors.length > 0 ? movie.mainActors.join(', ') : '—',
    },
  ];
}

/** Build the Album detail rows: Title, Band, ReleaseYear, Genre. */
function albumFields(album: AlbumDto): readonly DetailField[] {
  return [
    { label: 'Title', value: album.title },
    { label: 'Band', value: album.band ?? '—' },
    { label: 'Release Year', value: String(album.releaseYear) },
    { label: 'Genre', value: album.genre ?? '—' },
  ];
}
