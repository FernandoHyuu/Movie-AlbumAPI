import {
  Component,
  OnInit,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';

import { AlbumDto } from '../../core/models/album.model';
import { ProblemDetails } from '../../core/models/problem-details.model';
import { AlbumsService } from '../../core/services/albums.service';
import { NotificationService } from '../../core/services/notification.service';
import { CatalogGridComponent } from '../../shared/components/catalog-grid/catalog-grid.component';
import { DetailsModalComponent } from '../../shared/components/details-modal/details-modal.component';
import { MediaCardViewModel } from '../../shared/components/media-card/media-card.component';

/** Page size requested for the Album catalog. */
const ALBUMS_PAGE_SIZE = 24;

/**
 * Albums catalog view. Loads page 1 and projects each album into a media card
 * for the shared grid; selecting a card opens the details modal.
 */
@Component({
  selector: 'app-albums',
  imports: [CatalogGridComponent, DetailsModalComponent],
  template: `
    <section class="albums" aria-labelledby="albums-heading">
      <h1 id="albums-heading" class="albums__heading">Albums</h1>

      <app-catalog-grid
        [items]="cards()"
        emptyMessage="No albums available."
        (cardSelect)="onCardSelect($event)"
      />

      <app-details-modal />
    </section>
  `,
  styleUrl: './albums.component.css',
})
export class AlbumsComponent implements OnInit {
  private readonly albums = inject(AlbumsService);
  private readonly notifications = inject(NotificationService);

  @ViewChild(DetailsModalComponent) private modal?: DetailsModalComponent;

  /** Loaded page of albums, retained so a card selection can resolve its DTO. */
  private readonly items = signal<AlbumDto[]>([]);

  readonly cards = signal<readonly MediaCardViewModel[]>([]);

  ngOnInit(): void {
    this.loadFirstPage();
  }

  private loadFirstPage(): void {
    this.albums.getPage(1, ALBUMS_PAGE_SIZE).subscribe({
      next: (page) => {
        this.items.set(page.items);
        this.cards.set(page.items.map((album) => this.toCard(album)));
      },
      error: (error: unknown) => {
        this.notifications.error(this.toProblem(error));
      },
    });
  }

  onCardSelect(card: MediaCardViewModel): void {
    const album = this.items().find((candidate) => candidate.id === card.id);
    if (album) {
      this.modal?.open({ kind: 'album', item: album });
    }
  }

  private toCard(album: AlbumDto): MediaCardViewModel {
    return {
      id: album.id,
      title: album.title,
      subtitle: album.band,
      // coverUrl is null when the album has no cover, so the card shows a placeholder.
      coverUrl: this.albums.coverUrl(album),
    };
  }

  /** Pull the ProblemDetails body from an HTTP error; `null` for non-HTTP errors. */
  private toProblem(error: unknown): ProblemDetails | null {
    if (
      error instanceof HttpErrorResponse &&
      error.error &&
      typeof error.error === 'object'
    ) {
      return error.error as ProblemDetails;
    }
    return null;
  }
}
