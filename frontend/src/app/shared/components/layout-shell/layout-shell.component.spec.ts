import { provideHttpClient } from '@angular/common/http';
import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Breakpoint, BreakpointService } from '../../../core/services/breakpoint.service';
import { Role } from '../../../core/models/role.enum';
import { TokenService } from '../../../core/services/token.service';
import { LayoutShellComponent } from './layout-shell.component';

/**
 * Example tests for the responsive layout shell (R18.1–R18.5).
 *
 * The shell adapts to the viewport breakpoint reported by the
 * {@link BreakpointService}:
 * - desktop/tablet → sidebar permanently expanded, no mobile toggle (R18.1, R18.2);
 * - mobile → sidebar collapsed by default with a toggle that expands it (R18.3, R18.4);
 * - crossing from mobile back to desktop/tablet resets to the expanded,
 *   collapsed-baseline state (R18.5 state behaviour).
 *
 * Note: the exact Catalog_View grid column counts (≥4 on desktop, 2–3 on
 * tablet, 1 on mobile — R18.1–R18.3) are driven by CSS media queries in the
 * shared catalog-grid stylesheet. Those are not asserted here because they
 * require a real viewport to evaluate; they are verified by inspection. These
 * tests cover the JS-observable layout/sidebar logic the shell owns.
 */

/**
 * A BreakpointService stub backed by a writable signal the test controls, so a
 * breakpoint "crossing" is a single `set()` on the signal — the same reactive
 * path the real service uses via `matchMedia` listeners.
 */
class BreakpointServiceStub {
  readonly currentSignal: WritableSignal<Breakpoint> = signal<Breakpoint>('desktop');
  readonly current = this.currentSignal.asReadonly();

  isMobile(): boolean {
    return this.currentSignal() === 'mobile';
  }
}

/** A TokenService stub so the nested role-aware sidebar renders without a real token. */
class TokenServiceStub {
  readonly roleSignal: WritableSignal<Role | null> = signal<Role | null>(Role.User_Full);
  readonly role = this.roleSignal.asReadonly();
  readonly hasAccessTokenSignal: WritableSignal<boolean> = signal<boolean>(true);
  readonly hasAccessToken = this.hasAccessTokenSignal.asReadonly();
}

describe('LayoutShellComponent (responsive layout, R18)', () => {
  let breakpoints: BreakpointServiceStub;
  let fixture: ComponentFixture<LayoutShellComponent>;

  beforeEach(() => {
    breakpoints = new BreakpointServiceStub();

    TestBed.configureTestingModule({
      imports: [LayoutShellComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: BreakpointService, useValue: breakpoints },
        { provide: TokenService, useValue: new TokenServiceStub() },
      ],
    });

    fixture = TestBed.createComponent(LayoutShellComponent);
  });

  afterEach(() => fixture.destroy());

  /** The mobile toggle button, or null when it is not rendered. */
  function toggleButton(): HTMLButtonElement | null {
    const host = fixture.nativeElement as HTMLElement;
    return host.querySelector<HTMLButtonElement>('.layout__toggle');
  }

  /** The sidebar <aside> element. */
  function aside(): HTMLElement {
    const host = fixture.nativeElement as HTMLElement;
    const el = host.querySelector<HTMLElement>('.layout__sidebar');
    expect(el).not.toBeNull();
    return el as HTMLElement;
  }

  it('keeps the sidebar expanded with no toggle on desktop (R18.1)', () => {
    breakpoints.currentSignal.set('desktop');
    fixture.detectChanges();

    expect(fixture.componentInstance.isMobile()).toBeFalse();
    expect(fixture.componentInstance.sidebarExpanded()).toBeTrue();
    expect(toggleButton()).toBeNull();
    expect(aside().classList).toContain('layout__sidebar--expanded');
    // Not hidden on desktop.
    expect(aside().getAttribute('aria-hidden')).toBe('false');
  });

  it('keeps the sidebar expanded with no toggle on tablet (R18.2)', () => {
    breakpoints.currentSignal.set('tablet');
    fixture.detectChanges();

    expect(fixture.componentInstance.isMobile()).toBeFalse();
    expect(fixture.componentInstance.sidebarExpanded()).toBeTrue();
    expect(toggleButton()).toBeNull();
    expect(aside().classList).toContain('layout__sidebar--expanded');
  });

  it('collapses the sidebar by default and shows a toggle on mobile (R18.3)', () => {
    breakpoints.currentSignal.set('mobile');
    fixture.detectChanges();

    expect(fixture.componentInstance.isMobile()).toBeTrue();
    // Collapsed by default on mobile.
    expect(fixture.componentInstance.sidebarExpanded()).toBeFalse();
    expect(aside().classList).not.toContain('layout__sidebar--expanded');
    // Hidden from assistive tech while collapsed.
    expect(aside().getAttribute('aria-hidden')).toBe('true');
    // The toggle control is present.
    const toggle = toggleButton();
    expect(toggle).not.toBeNull();
    expect(toggle!.getAttribute('aria-expanded')).toBe('false');
  });

  it('expands the sidebar when the mobile toggle is activated (R18.4)', () => {
    breakpoints.currentSignal.set('mobile');
    fixture.detectChanges();

    const toggle = toggleButton();
    expect(toggle).not.toBeNull();

    toggle!.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.sidebarExpanded()).toBeTrue();
    expect(aside().classList).toContain('layout__sidebar--expanded');
    expect(aside().getAttribute('aria-hidden')).toBe('false');
    expect(toggle!.getAttribute('aria-expanded')).toBe('true');

    // Toggling again collapses it (switches between states).
    toggle!.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.sidebarExpanded()).toBeFalse();
    expect(aside().classList).not.toContain('layout__sidebar--expanded');
  });

  it('resets to the expanded, collapsed-baseline state when crossing mobile → desktop (R18.5)', () => {
    // Start on mobile and expand the sidebar.
    breakpoints.currentSignal.set('mobile');
    fixture.detectChanges();
    toggleButton()!.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.sidebarExpanded()).toBeTrue();

    // Cross the breakpoint back to desktop.
    breakpoints.currentSignal.set('desktop');
    fixture.detectChanges();

    // Sidebar is shown expanded on desktop and the mobile toggle disappears.
    expect(fixture.componentInstance.isMobile()).toBeFalse();
    expect(fixture.componentInstance.sidebarExpanded()).toBeTrue();
    expect(toggleButton()).toBeNull();

    // Re-entering mobile honors the collapsed-by-default baseline (R18.3) —
    // the earlier expanded state did not leak across the crossing.
    breakpoints.currentSignal.set('mobile');
    fixture.detectChanges();
    expect(fixture.componentInstance.sidebarExpanded()).toBeFalse();
    expect(toggleButton()!.getAttribute('aria-expanded')).toBe('false');
  });
});
