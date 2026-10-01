// Cover images are checked here before upload so the form can reject an
// oversized or wrong-format file without a round trip. The byte cap mirrors the
// backend's 5 MB limit; the client only accepts JPEG/PNG (the API also takes
// webp, but we keep the client stricter).

/** Maximum accepted cover-image size, in bytes (5 MB). */
export const MAX_COVER_BYTES = 5 * 1024 * 1024;

/** MIME types accepted for a cover image. */
export const ACCEPTED_COVER_TYPES: readonly string[] = ['image/jpeg', 'image/png'];

export const COVER_REQUIREMENTS_MESSAGE =
  'Cover image must be a JPEG or PNG no larger than 5 MB.';

export interface CoverValidationResult {
  valid: boolean;
  /** User-facing failure message, present when `valid` is `false`. */
  message?: string;
}

export function validateCoverFile(file: File): CoverValidationResult {
  if (!ACCEPTED_COVER_TYPES.includes(file.type)) {
    return { valid: false, message: COVER_REQUIREMENTS_MESSAGE };
  }
  if (file.size > MAX_COVER_BYTES) {
    return { valid: false, message: COVER_REQUIREMENTS_MESSAGE };
  }
  return { valid: true };
}

/**
 * Build the `multipart/form-data` body for the `{id}/cover` endpoint. The file
 * goes under the `file` field, which the backend binds to its cover parameter.
 */
export function buildCoverFormData(file: File): FormData {
  const form = new FormData();
  form.append('file', file, file.name);
  return form;
}
