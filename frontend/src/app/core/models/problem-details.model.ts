/**
 * RFC 7807 Problem Details models matching the API error contract
 * (`application/problem+json`). The `detail` field drives error toasts; a
 * missing or empty detail yields a generic failure message.
 */

/** A standard RFC 7807 Problem Details error body. */
export interface ProblemDetails {
  /** A URI reference identifying the problem type. */
  type?: string;
  /** A short, human-readable summary of the problem type. */
  title?: string;
  /** The HTTP status code generated for this occurrence. */
  status?: number;
  /** A human-readable explanation specific to this occurrence. */
  detail?: string;
  /** A URI reference identifying the specific occurrence. */
  instance?: string;
  /** Problem Details allows additional members. */
  [key: string]: unknown;
}

/**
 * Validation Problem Details returned for 400 responses (.NET
 * `ValidationProblemDetails` shape): `errors` maps each offending field name to
 * a non-empty array of human-readable messages.
 */
export interface ValidationProblemDetails extends ProblemDetails {
  errors: Record<string, string[]>;
}

/** Type guard narrowing a {@link ProblemDetails} to a {@link ValidationProblemDetails}. */
export function isValidationProblemDetails(
  problem: ProblemDetails,
): problem is ValidationProblemDetails {
  return (
    typeof problem['errors'] === 'object' &&
    problem['errors'] !== null &&
    !Array.isArray(problem['errors'])
  );
}
