import { ComponentFixture, TestBed } from '@angular/core/testing';
import fc from 'fast-check';
import { AlbumDto } from '../../../core/models/album.model';
import { MovieDto } from '../../../core/models/movie.model';
import { DetailsModalComponent } from './details-modal.component';

/**
 * Feature: streaming-panel, Property 32: The details modal renders all required
 * fields of the selected media.
 *
 * The Details_Modal must display the full field set for whichever media kind is
 * selected: for a Movie it shows Title, Studio, ReleaseYear, and MainActors
 * (R17.5); for an Album it shows Title, Band, ReleaseYear, and Genre (R17.6).
 * Optional fields that are null render as the em-dash placeholder '—', and the
 * list of MainActors is joined with ', '. These property tests generate
 * arbitrary movies and albums and assert the rendered dialog carries every
 * required label and value.
 *
 * Validates: Requirements 17.5, 17.6
 */

const PLACEHOLDER = '—';

/** The em-dash is the placeholder the component renders for a null optional field. */

/** A trimmed, non-empty string so DOM-text assertions stay meaningful. */
const nonEmptyText: fc.Arbitrary<string> = fc
  .string({ minLength: 1, maxLength: 60 })
  .map((s) => s.trim())
  .filter((s) => s.length > 0);

/** A GUID-shaped identifier; its exact value is irrelevant to the rendered fields. */
const idArb: fc.Arbitrary<string> = fc.uuid();

/** ReleaseYear within the system's valid range (1888–2100, R6.4 / R7.5). */
const releaseYearArb: fc.Arbitrary<number> = fc.integer({ min: 1888, max: 2100 });

/** An optional free-text field: either a trimmed non-empty string or null. */
const optionalText: fc.Arbitrary<string | null> = fc.option(nonEmptyText, {
  nil: null,
});

/** Arbitrary MovieDto with random title, optional studio, year, actors, cover. */
const movieArb: fc.Arbitrary<MovieDto> = fc.record({
  id: idArb,
  title: nonEmptyText,
  studio: optionalText,
  releaseYear: releaseYearArb,
  mainActors: fc.array(nonEmptyText, { maxLength: 6 }),
  hasCover: fc.boolean(),
});

/** Arbitrary AlbumDto with random title, optional band/genre, year, cover. */
const albumArb: fc.Arbitrary<AlbumDto> = fc.record({
  id: idArb,
  title: nonEmptyText,
  band: optionalText,
  releaseYear: releaseYearArb,
  genre: optionalText,
  hasCover: fc.boolean(),
});

describe('DetailsModalComponent (Feature: streaming-panel, Property 32: renders all required fields)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DetailsModalComponent],
    });
  });

  /** Read the rendered field rows as an ordered list of {label, value} pairs. */
  function readRows(
    fixture: ComponentFixture<DetailsModalComponent>,
  ): { label: string; value: string }[] {
    const host = fixture.nativeElement as HTMLElement;
    const dialog = host.querySelector('.details-modal');
    expect(dialog).not.toBeNull();
    const labels = Array.from(
      host.querySelectorAll<HTMLElement>('.details-modal__label'),
      (el) => el.textContent?.trim() ?? '',
    );
    const values = Array.from(
      host.querySelectorAll<HTMLElement>('.details-modal__value'),
      (el) => el.textContent?.trim() ?? '',
    );
    expect(labels.length).toBe(values.length);
    return labels.map((label, i) => ({ label, value: values[i] }));
  }

  /** The value rendered for a given label, or undefined if the label is absent. */
  function valueFor(
    rows: { label: string; value: string }[],
    label: string,
  ): string | undefined {
    return rows.find((r) => r.label === label)?.value;
  }

  it('Property 32: renders Title, Studio, Release Year, and Main Actors for any Movie (R17.5)', () => {
    fc.assert(
      fc.property(movieArb, (movie) => {
        // Fresh fixture per generated item so each movie renders independently.
        const fixture = TestBed.createComponent(DetailsModalComponent);
        try {
          fixture.componentInstance.open({ kind: 'movie', item: movie });
          fixture.detectChanges();

          const rows = readRows(fixture);
          const labels = rows.map((r) => r.label);

          // All required movie labels are present (R17.5).
          expect(labels).toContain('Title');
          expect(labels).toContain('Studio');
          expect(labels).toContain('Release Year');
          expect(labels).toContain('Main Actors');

          // Title appears verbatim.
          expect(valueFor(rows, 'Title')).toBe(movie.title);

          // Studio shows its value, or the placeholder when null.
          expect(valueFor(rows, 'Studio')).toBe(movie.studio ?? PLACEHOLDER);

          // The release year number appears in its row.
          const yearValue = valueFor(rows, 'Release Year') ?? '';
          expect(yearValue).toContain(String(movie.releaseYear));

          // Each actor name appears, or the placeholder when the list is empty.
          const actorsValue = valueFor(rows, 'Main Actors') ?? '';
          if (movie.mainActors.length === 0) {
            expect(actorsValue).toBe(PLACEHOLDER);
          } else {
            for (const actor of movie.mainActors) {
              expect(actorsValue).toContain(actor);
            }
          }
        } finally {
          fixture.destroy();
        }
      }),
      { numRuns: 100 },
    );
  });

  it('Property 32: renders Title, Band, Release Year, and Genre for any Album (R17.6)', () => {
    fc.assert(
      fc.property(albumArb, (album) => {
        // Fresh fixture per generated item so each album renders independently.
        const fixture = TestBed.createComponent(DetailsModalComponent);
        try {
          fixture.componentInstance.open({ kind: 'album', item: album });
          fixture.detectChanges();

          const rows = readRows(fixture);
          const labels = rows.map((r) => r.label);

          // All required album labels are present (R17.6).
          expect(labels).toContain('Title');
          expect(labels).toContain('Band');
          expect(labels).toContain('Release Year');
          expect(labels).toContain('Genre');

          // Title appears verbatim.
          expect(valueFor(rows, 'Title')).toBe(album.title);

          // Band and Genre show their value, or the placeholder when null.
          expect(valueFor(rows, 'Band')).toBe(album.band ?? PLACEHOLDER);
          expect(valueFor(rows, 'Genre')).toBe(album.genre ?? PLACEHOLDER);

          // The release year number appears in its row.
          const yearValue = valueFor(rows, 'Release Year') ?? '';
          expect(yearValue).toContain(String(album.releaseYear));
        } finally {
          fixture.destroy();
        }
      }),
      { numRuns: 100 },
    );
  });
});
