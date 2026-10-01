import { Component, Signal, inject } from '@angular/core';

import { SpinnerService } from '../../../core/services/spinner.service';

/**
 * App-wide loading overlay bound to {@link SpinnerService.visible}. Renders an
 * animated indicator exactly while an HTTP request is outstanding.
 */
@Component({
  selector: 'app-spinner-overlay',
  template: `
    @if (visible()) {
      <div class="spinner-overlay" role="status" aria-live="polite" aria-label="Loading">
        <div class="spinner-overlay__indicator"></div>
      </div>
    }
  `,
  styles: [
    `
      .spinner-overlay {
        position: fixed;
        inset: 0;
        z-index: 1000;
        display: flex;
        align-items: center;
        justify-content: center;
        background: rgba(0, 0, 0, 0.25);
      }
      .spinner-overlay__indicator {
        width: 48px;
        height: 48px;
        border: 5px solid rgba(255, 255, 255, 0.4);
        border-top-color: #fff;
        border-radius: 50%;
        animation: spinner-overlay-spin 0.8s linear infinite;
      }
      @keyframes spinner-overlay-spin {
        to {
          transform: rotate(360deg);
        }
      }
    `,
  ],
})
export class SpinnerOverlayComponent {
  private readonly spinner = inject(SpinnerService);

  /** `true` while one or more HTTP requests are in flight. */
  readonly visible: Signal<boolean> = this.spinner.visible;
}
