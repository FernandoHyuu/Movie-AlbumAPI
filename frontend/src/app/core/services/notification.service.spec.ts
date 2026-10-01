import { TestBed } from '@angular/core/testing';
import fc from 'fast-check';

import { ProblemDetails } from '../models/problem-details.model';
import {
  GENERIC_ERROR_MESSAGE,
  MAX_ERROR_MESSAGE_LENGTH,
  NotificationService,
} from './notification.service';

/**
 * Property-based test for toast derivation (Property 34).
 *
 * Feature: streaming-panel, Property 34: Error toasts derive from the Problem
 * Details detail.
 */
describe('NotificationService', () => {
  let service: NotificationService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(NotificationService);
  });

  /**
   * Generates a `detail` value spanning the input space the acceptance criteria
   * distinguish: absent (undefined), empty, whitespace-only, and non-empty
   * strings of varying length including values longer than the 500-char cap.
   */
  const detailArb: fc.Arbitrary<string | undefined> = fc.oneof(
    fc.constant(undefined),
    fc.constant(''),
    // Whitespace-only strings (trim to empty) → generic fallback (R19.6).
    fc.stringMatching(/^[ \t\n\r]{1,10}$/),
    // Non-empty content, including lengths well beyond MAX_ERROR_MESSAGE_LENGTH.
    fc.string({ minLength: 1, maxLength: MAX_ERROR_MESSAGE_LENGTH * 2 + 50 }),
  );

  /** An arbitrary Problem Details body carrying a (possibly absent) detail. */
  const problemArb: fc.Arbitrary<ProblemDetails> = fc.record({
    type: fc.option(fc.webUrl(), { nil: undefined }),
    title: fc.option(fc.string(), { nil: undefined }),
    status: fc.option(fc.integer({ min: 400, max: 599 }), { nil: undefined }),
    detail: detailArb,
  });

  /** Whether the derived message should fall back to the generic text. */
  function expectsGeneric(detail: string | undefined): boolean {
    return typeof detail !== 'string' || detail.trim().length === 0;
  }

  it('Property 34: error toasts derive from the Problem Details detail (R19.5, R19.6)', () => {
    // Validates: Requirements 19.5, 19.6
    fc.assert(
      fc.property(problemArb, (problem) => {
        service.clear();
        const detail = problem.detail;
        const derived = service.deriveErrorMessage(problem);

        if (expectsGeneric(detail)) {
          // Absent/empty/whitespace detail → generic failure message (R19.6).
          expect(derived).toBe(GENERIC_ERROR_MESSAGE);
        } else {
          // Non-empty detail → detail truncated to 500 chars (R19.5).
          expect(derived).toBe((detail as string).slice(0, MAX_ERROR_MESSAGE_LENGTH));
          // The result is at most 500 characters and a prefix of the detail.
          expect(derived.length).toBeLessThanOrEqual(MAX_ERROR_MESSAGE_LENGTH);
          expect((detail as string).startsWith(derived)).toBe(true);
        }

        // error(...) produces an 'error' toast whose message equals the derived
        // message, and the toast is added to the toasts() signal.
        const id = service.error(problem);
        const toasts = service.toasts();
        const created = toasts.find((t) => t.id === id);

        expect(created).toBeDefined();
        expect(created!.kind).toBe('error');
        expect(created!.message).toBe(derived);
        expect(toasts).toContain(created!);
      }),
      { numRuns: 100 },
    );
  });

  it('Property 34: plain-string details follow the same derivation (R19.5, R19.6)', () => {
    // Validates: Requirements 19.5, 19.6
    fc.assert(
      fc.property(detailArb, (detail) => {
        service.clear();
        const derived = service.deriveErrorMessage(detail);

        if (expectsGeneric(detail)) {
          expect(derived).toBe(GENERIC_ERROR_MESSAGE);
        } else {
          expect(derived).toBe((detail as string).slice(0, MAX_ERROR_MESSAGE_LENGTH));
          expect(derived.length).toBeLessThanOrEqual(MAX_ERROR_MESSAGE_LENGTH);
        }

        const id = service.error(detail ?? null);
        const created = service.toasts().find((t) => t.id === id);
        expect(created).toBeDefined();
        expect(created!.kind).toBe('error');
        expect(created!.message).toBe(service.deriveErrorMessage(detail ?? null));
      }),
      { numRuns: 100 },
    );
  });

  it('treats null and undefined as a generic error (R19.6)', () => {
    expect(service.deriveErrorMessage(null)).toBe(GENERIC_ERROR_MESSAGE);
    expect(service.deriveErrorMessage(undefined)).toBe(GENERIC_ERROR_MESSAGE);
  });

  it('truncates an over-long detail to exactly 500 characters (R19.5)', () => {
    const detail = 'x'.repeat(MAX_ERROR_MESSAGE_LENGTH + 123);
    const derived = service.deriveErrorMessage({ detail });
    expect(derived.length).toBe(MAX_ERROR_MESSAGE_LENGTH);
    expect(derived).toBe('x'.repeat(MAX_ERROR_MESSAGE_LENGTH));
  });
});
