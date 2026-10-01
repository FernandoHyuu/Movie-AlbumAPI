import { DestroyRef, Injectable, Signal, inject, signal } from '@angular/core';

/** The named viewport ranges that drive responsive layout decisions. */
export type Breakpoint = 'mobile' | 'tablet' | 'desktop';

/** Lower bound, in px, of the tablet range (and the mobile/tablet divide). */
export const TABLET_MIN_WIDTH_PX = 768;
/** Lower bound, in px, of the desktop range (and the tablet/desktop divide). */
export const DESKTOP_MIN_WIDTH_PX = 1024;

const DESKTOP_QUERY = `(min-width: ${DESKTOP_MIN_WIDTH_PX}px)`;
const TABLET_QUERY = `(min-width: ${TABLET_MIN_WIDTH_PX}px) and (max-width: ${DESKTOP_MIN_WIDTH_PX - 1}px)`;

/**
 * Tracks the active {@link Breakpoint} as a signal, derived from viewport width.
 *
 * We use `matchMedia` listeners rather than polling resize events: the signal
 * updates synchronously the instant a boundary is crossed, so bound views
 * re-render in the same change-detection cycle. The service is SSR-safe — when
 * `window.matchMedia` is unavailable it defaults to `desktop` and registers no
 * listeners.
 */
@Injectable({ providedIn: 'root' })
export class BreakpointService {
  private readonly destroyRef = inject(DestroyRef);

  private readonly breakpoint = signal<Breakpoint>(this.resolveInitial());

  /** The current responsive breakpoint. */
  readonly current: Signal<Breakpoint> = this.breakpoint.asReadonly();

  constructor() {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return;
    }

    const desktop = window.matchMedia(DESKTOP_QUERY);
    const tablet = window.matchMedia(TABLET_QUERY);

    const update = (): void => {
      this.breakpoint.set(desktop.matches ? 'desktop' : tablet.matches ? 'tablet' : 'mobile');
    };

    desktop.addEventListener('change', update);
    tablet.addEventListener('change', update);
    update();

    this.destroyRef.onDestroy(() => {
      desktop.removeEventListener('change', update);
      tablet.removeEventListener('change', update);
    });
  }

  /** `true` while the viewport is in the mobile range (width < 768 px). */
  isMobile(): boolean {
    return this.breakpoint() === 'mobile';
  }

  /** Resolves the breakpoint from current width, defaulting to `desktop` under SSR. */
  private resolveInitial(): Breakpoint {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return 'desktop';
    }
    if (window.matchMedia(DESKTOP_QUERY).matches) {
      return 'desktop';
    }
    if (window.matchMedia(TABLET_QUERY).matches) {
      return 'tablet';
    }
    return 'mobile';
  }
}
