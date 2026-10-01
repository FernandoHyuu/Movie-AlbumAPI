import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import {
  MediaCardComponent,
  MediaCardViewModel,
} from '../media-card/media-card.component';

/**
 * Responsive grid of {@link MediaCardComponent} cards. Takes a media-agnostic
 * list of view models so it serves both the Movies and Albums catalogs, and
 * renders an empty-state message when the list is empty.
 */
@Component({
  selector: 'app-catalog-grid',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MediaCardComponent],
  template: `
    @if (items().length > 0) {
      <div class="catalog-grid" role="list">
        @for (item of items(); track item.id) {
          <app-media-card
            role="listitem"
            [card]="item"
            (select)="cardSelect.emit($event)"
          />
        }
      </div>
    } @else {
      <p class="catalog-grid__empty" role="status">
        {{ emptyMessage() }}
      </p>
    }
  `,
  styleUrl: './catalog-grid.component.css',
})
export class CatalogGridComponent {
  /** The catalog entries to render as cards. */
  readonly items = input.required<readonly MediaCardViewModel[]>();

  /** Message shown when the catalog has no entries. */
  readonly emptyMessage = input<string>('No items are available.');

  /** Emits the view model of the card the user selected. */
  readonly cardSelect = output<MediaCardViewModel>();
}
