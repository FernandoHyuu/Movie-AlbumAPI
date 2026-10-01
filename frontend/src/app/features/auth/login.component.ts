import {
  Component,
  WritableSignal,
  inject,
  signal,
} from '@angular/core';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';

import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { ProblemDetails } from '../../core/models/problem-details.model';

/** Fallback route used after login when no `returnUrl` is supplied. */
export const DEFAULT_POST_LOGIN_ROUTE = '/movies';

/**
 * Login view. Submits email + password via {@link AuthService.login}; on success
 * navigates to the preserved `returnUrl` (or the default route), on failure shows
 * an error toast and keeps the form editable. A pending signal disables the form
 * while the request is in flight to block duplicate submissions.
 */
@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  template: `
    <section class="login" aria-labelledby="login-heading">
      <h1 id="login-heading" class="login__heading">Sign in</h1>

      <form class="login__form" [formGroup]="form" (ngSubmit)="submit()">
        <label class="login__field">
          <span class="login__label">Email</span>
          <input
            type="email"
            formControlName="email"
            autocomplete="email"
            class="login__input"
            [attr.aria-invalid]="isInvalid('email') ? 'true' : null"
          />
          @if (isInvalid('email')) {
            <span class="login__error" role="alert">
              Enter a valid email address.
            </span>
          }
        </label>

        <label class="login__field">
          <span class="login__label">Password</span>
          <input
            type="password"
            formControlName="password"
            autocomplete="current-password"
            class="login__input"
            [attr.aria-invalid]="isInvalid('password') ? 'true' : null"
          />
          @if (isInvalid('password')) {
            <span class="login__error" role="alert">
              Password is required.
            </span>
          }
        </label>

        <button
          type="submit"
          class="login__submit"
          [disabled]="form.invalid || pending()"
        >
          {{ pending() ? 'Signing in…' : 'Sign in' }}
        </button>
      </form>
    </section>
  `,
  styleUrl: './login.component.css',
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly notifications = inject(NotificationService);

  /** `true` while a login request is in flight; disables the form to block resubmits. */
  readonly pending: WritableSignal<boolean> = signal(false);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  /** `true` when a control is invalid and has been touched or changed. */
  isInvalid(controlName: 'email' | 'password'): boolean {
    const control = this.form.controls[controlName];
    return control.invalid && (control.touched || control.dirty);
  }

  submit(): void {
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.pending.set(true);
    const credentials = this.form.getRawValue();

    this.auth.login(credentials).subscribe({
      next: () => {
        this.pending.set(false);
        void this.router.navigateByUrl(this.resolveReturnUrl());
      },
      error: (error: unknown) => {
        this.pending.set(false);
        this.notifications.error(this.toProblem(error));
      },
    });
  }

  /**
   * Honor the `returnUrl` the guard stored when it redirected the user here, so
   * they land back where they were headed; fall back to the default route.
   */
  private resolveReturnUrl(): string {
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    return returnUrl && returnUrl.length > 0 ? returnUrl : DEFAULT_POST_LOGIN_ROUTE;
  }

  /** Pull the ProblemDetails body from an HTTP error; `null` for non-HTTP errors. */
  private toProblem(error: unknown): ProblemDetails | null {
    if (error instanceof HttpErrorResponse && error.error && typeof error.error === 'object') {
      return error.error as ProblemDetails;
    }
    return null;
  }
}
