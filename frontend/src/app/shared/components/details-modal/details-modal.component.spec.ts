import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MovieDto } from '../../../core/models/movie.model';
import {
  DetailsModalComponent,
  SelectedMedia,
} from './details-modal.component';

/**
 * Example tests for the Details_Modal focus-return behaviour (R17.7).
 *
 * When the modal closes — whether via {@link DetailsModalComponent.close},
 * the close button, the backdrop, or Escape — focus must return to the element
 * that was focused when the modal opened (the triggering grid card), so a
 * keyboard user lands back on the Catalog_View grid (R17.7). The `closed`
 * output must fire so the host can react.
 */
describe('DetailsModalComponent (focus return, R17.7)', () => {
  let fixture: ComponentFixture<DetailsModalComponent>;
  let component: DetailsModalComponent;
  /** A stand-in for the grid card that opened the modal; focus must return here. */
  let trigger: HTMLButtonElement;

  const SAMPLE_MOVIE: MovieDto = {
    id: '11111111-1111-1111-1111-111111111111',
    title: 'Blade Runner 2049',
    studio: 'Warner Bros.',
    releaseYear: 2017,
    mainActors: ['Ryan Gosling', 'Harrison Ford'],
    hasCover: true,
  };

  const MOVIE_SELECTION: SelectedMedia = { kind: 'movie', item: SAMPLE_MOVIE };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DetailsModalComponent],
    });

    // A real, focusable element attached to the live document so that
    // document.activeElement reflects it and .focus() actually moves focus.
    trigger = document.createElement('button');
    trigger.textContent = 'Open details';
    document.body.appendChild(trigger);

    fixture = TestBed.createComponent(DetailsModalComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    fixture.destroy();
    trigger.remove();
  });

  /**
   * Open the modal as if the trigger card were the active element, then flush
   * the microtask the component schedules to move focus into the dialog.
   */
  async function openFromTrigger(): Promise<HTMLElement> {
    trigger.focus();
    expect(document.activeElement).toBe(trigger);

    component.open(MOVIE_SELECTION);
    fixture.detectChanges();

    // open() moves focus into the dialog on the next microtask.
    await Promise.resolve();

    const host = fixture.nativeElement as HTMLElement;
    const dialog = host.querySelector<HTMLElement>('.details-modal');
    expect(dialog).not.toBeNull();
    return dialog as HTMLElement;
  }

  it('returns focus to the opening element and emits closed on close()', async () => {
    const closedSpy = jasmine.createSpy('closed');
    component.closed.subscribe(closedSpy);

    await openFromTrigger();

    component.close();
    fixture.detectChanges();

    // Focus is restored to the element that opened the modal (R17.7).
    expect(document.activeElement).toBe(trigger);
    // The modal is no longer rendered.
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.details-modal')).toBeNull();
    // The host is notified exactly once.
    expect(closedSpy).toHaveBeenCalledTimes(1);
  });

  it('returns focus to the opening element when dismissed with Escape', async () => {
    const closedSpy = jasmine.createSpy('closed');
    component.closed.subscribe(closedSpy);

    const dialog = await openFromTrigger();

    // Escape on the dialog is bound to close() in the template.
    dialog.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }),
    );
    fixture.detectChanges();

    expect(document.activeElement).toBe(trigger);
    expect((fixture.nativeElement as HTMLElement).querySelector('.details-modal')).toBeNull();
    expect(closedSpy).toHaveBeenCalledTimes(1);
  });

  it('returns focus to the opening element when the close button is clicked', async () => {
    const closedSpy = jasmine.createSpy('closed');
    component.closed.subscribe(closedSpy);

    await openFromTrigger();

    const host = fixture.nativeElement as HTMLElement;
    const closeButton = host.querySelector<HTMLButtonElement>('.details-modal__close');
    expect(closeButton).not.toBeNull();
    closeButton!.click();
    fixture.detectChanges();

    expect(document.activeElement).toBe(trigger);
    expect(host.querySelector('.details-modal')).toBeNull();
    expect(closedSpy).toHaveBeenCalledTimes(1);
  });

  it('does not emit closed when close() is called while already closed', () => {
    const closedSpy = jasmine.createSpy('closed');
    component.closed.subscribe(closedSpy);

    // The modal was never opened; close() must be a no-op.
    component.close();
    fixture.detectChanges();

    expect(closedSpy).not.toHaveBeenCalled();
  });
});
