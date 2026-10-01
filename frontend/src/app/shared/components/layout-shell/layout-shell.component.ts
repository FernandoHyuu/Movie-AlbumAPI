import { Component, Signal, computed, effect, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { BreakpointService } from '../../../core/services/breakpoint.service';
import { SidebarComponent } from '../sidebar/sidebar.component';
import { SpinnerOverlayComponent } from '../spinner-overlay/spinner-overlay.component';
import { ToastContainerComponent } from '../toast-container/toast-container.component';

/**
 * Layout shell hosting the role-aware {@link SidebarComponent} around the routed
 * content, plus the app-wide spinner overlay and toast container.
 *
 * On tablet/desktop the sidebar is always expanded; on mobile it's collapsible
 * and starts collapsed. {@link BreakpointService} drives this through signals,
 * and sizing lives in CSS media queries, so crossing a breakpoint reflows the
 * existing DOM rather than tearing it down — preserving scroll and selection.
 */
@Component({
  selector: 'app-layout-shell',
  imports: [RouterOutlet, SidebarComponent, SpinnerOverlayComponent, ToastContainerComponent],
  template: `
    <div class="layout" [class.layout--mobile]="isMobile()">
      <header class="layout__bar">
        @if (isMobile()) {
          <button
            type="button"
            class="layout__toggle"
            (click)="toggleSidebar()"
            [attr.aria-expanded]="sidebarExpanded()"
            aria-controls="layout-sidebar"
            aria-label="Toggle navigation menu"
          >
            ☰
          </button>
        }
      </header>

      <aside
        id="layout-sidebar"
        class="layout__sidebar"
        [class.layout__sidebar--expanded]="sidebarExpanded()"
        [attr.aria-hidden]="isMobile() && !sidebarExpanded()"
      >
        <app-sidebar />
      </aside>

      <main class="layout__content">
        <router-outlet />
      </main>
    </div>

    <app-spinner-overlay />
    <app-toast-container />
  `,
  styleUrl: './layout-shell.component.css',
})
export class LayoutShellComponent {
  private readonly breakpoints = inject(BreakpointService);

  /** `true` while the viewport is in the mobile range (< 768 px). */
  readonly isMobile: Signal<boolean> = computed(() => this.breakpoints.current() === 'mobile');

  /** Whether the mobile sidebar is expanded. Only meaningful on mobile; starts collapsed. */
  private readonly mobileExpanded = signal(false);

  /** Whether the sidebar is visually expanded: always on tablet/desktop, toggle-driven on mobile. */
  readonly sidebarExpanded: Signal<boolean> = computed(
    () => !this.isMobile() || this.mobileExpanded(),
  );

  constructor() {
    // Leaving mobile resets to collapsed so re-entering mobile starts collapsed.
    effect(() => {
      if (!this.isMobile()) {
        this.mobileExpanded.set(false);
      }
    });
  }

  /** Toggle the sidebar collapsed/expanded. Only meaningful on mobile. */
  toggleSidebar(): void {
    this.mobileExpanded.update((expanded) => !expanded);
  }
}
