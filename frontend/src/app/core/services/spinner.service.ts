import { Injectable, Signal, computed, signal } from '@angular/core';

/**
 * Tracks in-flight HTTP requests and exposes a {@link visible} signal that is
 * `true` while at least one request is outstanding. A counter (not a boolean)
 * is used so concurrent requests don't hide the spinner prematurely — it stays
 * visible until the last request completes.
 */
@Injectable({ providedIn: 'root' })
export class SpinnerService {
  /** Number of HTTP requests currently in flight. Never negative. */
  private readonly inFlight = signal(0);

  /** `true` while one or more requests are in flight; `false` otherwise. */
  readonly visible: Signal<boolean> = computed(() => this.inFlight() > 0);

  /** The current in-flight request count (primarily for testing/diagnostics). */
  readonly count: Signal<number> = this.inFlight.asReadonly();

  /** Register the start of a request, incrementing the in-flight counter. */
  show(): void {
    this.inFlight.update((n) => n + 1);
  }

  /**
   * Register the completion of a request, decrementing the in-flight counter.
   * Clamped at zero so an unbalanced call can never drive the count negative or
   * leave the spinner stuck visible.
   */
  hide(): void {
    this.inFlight.update((n) => (n > 0 ? n - 1 : 0));
  }
}
