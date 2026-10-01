import { Injectable, Signal, signal } from '@angular/core';
import { ProblemDetails } from '../models/problem-details.model';

/** The visual severity of a toast, driving its styling in the Toast component. */
export type ToastKind = 'success' | 'error';

/** A single toast notification rendered by the shared Toast component. */
export interface Toast {
  /** Stable identifier used for rendering keys and {@link NotificationService.dismiss}. */
  readonly id: number;
  /** Severity controlling the toast's appearance. */
  readonly kind: ToastKind;
  /** The message shown to the user. */
  readonly message: string;
}

/** Minimum time, in ms, a success toast stays visible. */
export const MIN_TOAST_DURATION_MS = 3000;
/** Maximum time, in ms, a success toast stays visible. */
export const MAX_TOAST_DURATION_MS = 10000;
/** Default success-toast duration, within the permitted 3–10 s window. */
export const DEFAULT_TOAST_DURATION_MS = 5000;

/** Maximum length of an error message derived from Problem Details `detail`. */
export const MAX_ERROR_MESSAGE_LENGTH = 500;

/** Fallback shown when a Problem Details response carries no usable detail. */
export const GENERIC_ERROR_MESSAGE = 'The operation failed. Please try again.';

/**
 * Central toast store rendered by the shared Toast component. Success toasts
 * auto-dismiss after a duration clamped to the 3–10 s window; error toasts are
 * derived from an RFC 7807 `detail` (truncated to 500 chars, with a generic
 * fallback) and persist until dismissed so a failure isn't missed.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  /** Backing store of currently visible toasts, in insertion order. */
  private readonly toastList = signal<readonly Toast[]>([]);

  /** Monotonically increasing id source so every toast gets a unique id. */
  private nextId = 0;

  /** The toasts the Toast component should currently render. */
  readonly toasts: Signal<readonly Toast[]> = this.toastList.asReadonly();

  /**
   * Show a success toast that auto-dismisses after {@link durationMs}, clamped
   * into the 3–10 s window. Returns the toast id, usable with {@link dismiss}.
   */
  success(message: string, durationMs: number = DEFAULT_TOAST_DURATION_MS): number {
    const id = this.add('success', message);
    const clamped = this.clampDuration(durationMs);
    setTimeout(() => this.dismiss(id), clamped);
    return id;
  }

  /**
   * Show an error toast derived from a {@link ProblemDetails} `detail` (or a
   * plain string), truncated with a generic fallback. Error toasts do not
   * auto-dismiss. Returns the toast id, usable with {@link dismiss}.
   */
  error(problemOrMessage: ProblemDetails | string | null | undefined): number {
    const message = this.deriveErrorMessage(problemOrMessage);
    return this.add('error', message);
  }

  /** Remove the toast with the given id, if it is still present. */
  dismiss(id: number): void {
    this.toastList.update((toasts) => toasts.filter((t) => t.id !== id));
  }

  /** Remove all active toasts. */
  clear(): void {
    this.toastList.set([]);
  }

  /** Derive the toast message from a Problem Details detail or plain string. */
  deriveErrorMessage(problemOrMessage: ProblemDetails | string | null | undefined): string {
    const detail =
      typeof problemOrMessage === 'string'
        ? problemOrMessage
        : problemOrMessage?.detail;

    if (typeof detail === 'string' && detail.trim().length > 0) {
      return detail.slice(0, MAX_ERROR_MESSAGE_LENGTH);
    }
    return GENERIC_ERROR_MESSAGE;
  }

  /** Append a toast and return its id. */
  private add(kind: ToastKind, message: string): number {
    const id = this.nextId++;
    const toast: Toast = { id, kind, message };
    this.toastList.update((toasts) => [...toasts, toast]);
    return id;
  }

  /** Clamp a requested duration into the permitted 3–10 s window. */
  private clampDuration(durationMs: number): number {
    if (Number.isNaN(durationMs)) {
      return DEFAULT_TOAST_DURATION_MS;
    }
    return Math.min(MAX_TOAST_DURATION_MS, Math.max(MIN_TOAST_DURATION_MS, durationMs));
  }
}
