import { HttpErrorResponse } from '@angular/common/http';

import { ProblemDetails } from '../../../core/models/problem-details.model';

/**
 * Pull the ProblemDetails body out of an HTTP error so the notification service
 * can show its message. Non-HTTP errors (or responses without a JSON body)
 * return `null`, which renders as a generic failure message.
 */
export function toProblemDetails(error: unknown): ProblemDetails | null {
  if (
    error instanceof HttpErrorResponse &&
    error.error &&
    typeof error.error === 'object'
  ) {
    return error.error as ProblemDetails;
  }
  return null;
}
