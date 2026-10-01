import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';

/**
 * View model for a single catalog card, reused for both Movies and Albums. The
 * card stays media-agnostic by receiving a fully-built `coverUrl` (or `null`
 * for no cover) rather than constructing it itself.
 */
export interface MediaCardViewModel {
  /** Stable identifier of the underlying media record. */
  readonly id: string;
  /** Primary label shown on the card (the media Title). */
  readonly title: string;
  /** Cover endpoint URL, or `null` when the entry has no cover (renders a placeholder). */
  readonly coverUrl: string | null;
  /** Optional secondary label (e.g. Studio for movies, Band for albums). */
  readonly subtitle?: string | null;
}

/**
 * Presentational card for a single catalog entry.
 *
 * The cover endpoint is protected by the Movie/Album auth policy, so we fetch
 * the image through {@link HttpClient} (which runs the authInterceptor and
 * attaches the Bearer token) and bind the resulting blob object URL to the
 * `<img>`. A plain `<img src>` can't send the header, so the API would reject
 * it with 401. On no cover or a failed fetch, the card shows a placeholder.
 */
@Component({
  selector: 'app-media-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="media-card"
      (click)="activate()"
      [attr.aria-label]="'Open details for ' + card().title"
    >
      <div class="media-card__cover">
        @if (objectUrl()) {
          <img
            class="media-card__image"
            [src]="objectUrl()"
            [alt]="'Cover for ' + card().title"
          />
        } @else {
          <div class="media-card__placeholder" aria-hidden="true">
            <svg viewBox="0 0 24 24" class="media-card__placeholder-icon">
              <path
                d="M4 5h16a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1Zm1 2v8.59l3.3-3.3a1 1 0 0 1 1.4 0l2.3 2.3 3.3-3.3a1 1 0 0 1 1.4 0L19 15.6V7H5Zm4 2.5A1.5 1.5 0 1 1 7.5 11 1.5 1.5 0 0 1 9 9.5Z"
              />
            </svg>
            <span class="media-card__placeholder-label">No cover</span>
          </div>
        }
      </div>
      <div class="media-card__body">
        <span class="media-card__title">{{ card().title }}</span>
        @if (card().subtitle) {
          <span class="media-card__subtitle">{{ card().subtitle }}</span>
        }
      </div>
    </button>
  `,
  styleUrl: './media-card.component.css',
})
export class MediaCardComponent {
  private readonly http = inject(HttpClient);

  /** The card view model to render. */
  readonly card = input.required<MediaCardViewModel>();

  /** Emits the selected view model when the card is activated. */
  readonly select = output<MediaCardViewModel>();

  /**
   * Blob object URL for the fetched cover, or `null` for no cover / failed
   * fetch. A signal so the OnPush card re-renders when the fetch resolves.
   */
  readonly objectUrl = signal<string | null>(null);

  /** The last fetched `coverUrl`, to skip redundant refetches when the effect re-runs. */
  private loadedUrl: string | null = null;

  constructor() {
    // Reload the cover whenever coverUrl changes, fetching via HttpClient so the
    // auth interceptor can attach the Bearer token the endpoint requires.
    effect(() => {
      const coverUrl = this.card().coverUrl;
      if (coverUrl === this.loadedUrl) {
        return;
      }
      this.loadedUrl = coverUrl;
      this.setObjectUrl(null);

      if (!coverUrl) {
        return;
      }

      const requestedUrl = coverUrl;
      this.http.get(coverUrl, { responseType: 'blob' }).subscribe({
        next: (blob) => {
          // Ignore a stale response if the card changed while this was in flight.
          if (this.loadedUrl !== requestedUrl) {
            return;
          }
          this.setObjectUrl(URL.createObjectURL(blob));
        },
        error: () => {
          if (this.loadedUrl !== requestedUrl) {
            return;
          }
          // Fall back to the placeholder on any failure (e.g. 401/404).
          this.setObjectUrl(null);
        },
      });
    });

    // Revoke the outstanding object URL on destroy so the blob isn't leaked.
    inject(DestroyRef).onDestroy(() => this.setObjectUrl(null));
  }

  /** Swap in a new object URL, revoking the previous one so the blob is freed. */
  private setObjectUrl(next: string | null): void {
    const previous = this.objectUrl();
    if (previous && previous !== next) {
      URL.revokeObjectURL(previous);
    }
    this.objectUrl.set(next);
  }

  /** Emits the selection of this card to the parent grid. */
  activate(): void {
    this.select.emit(this.card());
  }
}
