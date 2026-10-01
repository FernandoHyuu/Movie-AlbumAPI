import {
  Component,
  OnInit,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';

import { MovieDto } from '../../core/models/movie.model';
import { ProblemDetails } from '../../core/models/problem-details.model';
import { MoviesService } from '../../core/services/movies.service';
import { NotificationService } from '../../core/services/notification.service';
import { CatalogGridComponent } from '../../shared/components/catalog-grid/catalog-grid.component';
import { DetailsModalComponent } from '../../shared/components/details-modal/details-modal.component';
import { MediaCardViewModel } from '../../shared/components/media-card/media-card.component';

/** Page size requested for the Movie catalog. */
const MOVIES_PAGE_SIZE = 24;

/**
 * Movies catalog view. Loads page 1 and projects each movie into a media card
 * for the shared grid; selecting a card opens the details modal.
 */
@Component({
  selector: 'app-movies',
  imports: [CatalogGridComponent, DetailsModalComponent],
  template: `
    <section class="movies" aria-labelledby="movies-heading">
      <h1 id="movies-heading" class="movies__heading">Movies</h1>

      <app-catalog-grid
        [items]="cards()"
        emptyMessage="No movies available."
        (cardSelect)="onCardSelect($event)"
      />

      <app-details-modal />
    </section>
  `,
  styleUrl: './movies.component.css',
})
export class MoviesComponent implements OnInit {
  private readonly movies = inject(MoviesService);
  private readonly notifications = inject(NotificationService);

  @ViewChild(DetailsModalComponent) private modal?: DetailsModalComponent;

  /** Loaded page of movies, retained so a card selection can resolve its DTO. */
  private readonly items = signal<MovieDto[]>([]);

  readonly cards = signal<readonly MediaCardViewModel[]>([]);

  ngOnInit(): void {
    this.loadFirstPage();
  }

  private loadFirstPage(): void {
    this.movies.getPage(1, MOVIES_PAGE_SIZE).subscribe({
      next: (page) => {
        this.items.set(page.items);
        this.cards.set(page.items.map((movie) => this.toCard(movie)));
      },
      error: (error: unknown) => {
        this.notifications.error(this.toProblem(error));
      },
    });
  }

  onCardSelect(card: MediaCardViewModel): void {
    const movie = this.items().find((candidate) => candidate.id === card.id);
    if (movie) {
      this.modal?.open({ kind: 'movie', item: movie });
    }
  }

  private toCard(movie: MovieDto): MediaCardViewModel {
    return {
      id: movie.id,
      title: movie.title,
      subtitle: movie.studio,
      // coverUrl is null when the movie has no cover, so the card shows a placeholder.
      coverUrl: this.movies.coverUrl(movie),
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
