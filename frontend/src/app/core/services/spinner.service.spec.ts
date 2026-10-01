import { TestBed } from '@angular/core/testing';
import * as fc from 'fast-check';

import { SpinnerService } from './spinner.service';

describe('SpinnerService', () => {
  let service: SpinnerService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(SpinnerService);
  });

  it('starts hidden with a zero count', () => {
    expect(service.count()).toBe(0);
    expect(service.visible()).toBe(false);
  });

  it('stays visible while concurrent requests overlap and hides on the last completion', () => {
    service.show();
    service.show();
    expect(service.visible()).toBe(true);

    service.hide();
    expect(service.visible()).toBe(true); // one still in flight

    service.hide();
    expect(service.count()).toBe(0);
    expect(service.visible()).toBe(false);
  });

  it('never drives the count negative on unbalanced hide() calls', () => {
    service.hide();
    service.hide();
    expect(service.count()).toBe(0);
    expect(service.visible()).toBe(false);
  });

  /**
   * Property 33: Spinner visibility tracks in-flight request count.
   *
   * For any interleaved sequence of request-start (show) and request-complete
   * (hide) events, the spinner is visible if and only if the number of
   * in-flight requests is greater than zero. The counter is floored at zero so
   * extra hide() calls never make the count negative nor leave the spinner
   * stuck visible.
   *
   * **Validates: Requirements 19.2**
   */
  it('is visible exactly when the net in-flight request count is > 0 (Property 33)', () => {
    fc.assert(
      fc.property(
        // true = show (request start), false = hide (request complete)
        fc.array(fc.boolean(), { minLength: 1, maxLength: 200 }),
        (operations) => {
          // Fresh service per run so no state leaks between runs.
          const svc = new SpinnerService();
          let expectedCount = 0;

          expect(svc.count()).toBe(0);
          expect(svc.visible()).toBe(false);

          for (const isShow of operations) {
            if (isShow) {
              svc.show();
              expectedCount += 1;
            } else {
              svc.hide();
              expectedCount = expectedCount > 0 ? expectedCount - 1 : 0;
            }

            // Count never goes negative.
            expect(expectedCount).toBeGreaterThanOrEqual(0);
            expect(svc.count()).toBe(expectedCount);
            // Visible iff there is at least one outstanding request.
            expect(svc.visible()).toBe(expectedCount > 0);
          }
        },
      ),
      { numRuns: 200 },
    );
  });
});
