import { Component, Signal, inject } from '@angular/core';

import { NotificationService, Toast } from '../../../core/services/notification.service';

/**
 * App-wide toast list bound to {@link NotificationService.toasts}, so feedback
 * raised anywhere is visible regardless of route. Each toast has a dismiss
 * control; success toasts also auto-dismiss on the service's timer.
 */
@Component({
  selector: 'app-toast-container',
  template: `
    <div class="toasts" aria-live="polite" aria-atomic="false">
      @for (toast of toasts(); track toast.id) {
        <div
          class="toast"
          [class.toast--success]="toast.kind === 'success'"
          [class.toast--error]="toast.kind === 'error'"
          [attr.role]="toast.kind === 'error' ? 'alert' : 'status'"
        >
          <span class="toast__message">{{ toast.message }}</span>
          <button
            type="button"
            class="toast__dismiss"
            (click)="dismiss(toast.id)"
            aria-label="Dismiss notification"
          >
            ×
          </button>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .toasts {
        position: fixed;
        top: 1rem;
        right: 1rem;
        z-index: 1100;
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
        max-width: min(90vw, 420px);
      }
      .toast {
        display: flex;
        align-items: flex-start;
        gap: 0.5rem;
        padding: 0.75rem 1rem;
        border-radius: 6px;
        color: #fff;
        box-shadow: 0 2px 8px rgba(0, 0, 0, 0.2);
      }
      .toast--success {
        background: #2e7d32;
      }
      .toast--error {
        background: #c62828;
      }
      .toast__message {
        flex: 1;
        word-break: break-word;
      }
      .toast__dismiss {
        background: transparent;
        border: none;
        color: inherit;
        font-size: 1.1rem;
        line-height: 1;
        cursor: pointer;
      }
    `,
  ],
})
export class ToastContainerComponent {
  private readonly notifications = inject(NotificationService);

  /** The toasts currently active in the notification store. */
  readonly toasts: Signal<readonly Toast[]> = this.notifications.toasts;

  /** Dismiss the toast with the given id. */
  dismiss(id: number): void {
    this.notifications.dismiss(id);
  }
}
