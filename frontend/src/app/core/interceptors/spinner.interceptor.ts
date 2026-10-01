import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize, timeout } from 'rxjs';

import { SpinnerService } from '../services/spinner.service';

/**
 * Drives the loading {@link SpinnerService} and enforces a hard request
 * deadline. Each request bumps the in-flight counter on start and clears it in
 * `finalize`, which runs on success, error, timeout, and unsubscription so the
 * counter always stays balanced.
 */

/** Hard per-request deadline in milliseconds. */
const REQUEST_TIMEOUT_MS = 30_000;

export const spinnerInterceptor: HttpInterceptorFn = (req, next) => {
  const spinner = inject(SpinnerService);

  spinner.show();

  return next(req).pipe(
    timeout(REQUEST_TIMEOUT_MS),
    finalize(() => spinner.hide()),
  );
};
