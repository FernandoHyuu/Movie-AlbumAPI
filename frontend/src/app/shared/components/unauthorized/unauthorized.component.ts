import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Minimal "not authorized" view the {@link roleGuard} redirects to when a role
 * doesn't permit the attempted route. Shows a forbidden message and a link back
 * to the catalog.
 */
@Component({
  selector: 'app-unauthorized',
  imports: [RouterLink],
  template: `
    <section class="unauthorized" role="alert" aria-labelledby="unauthorized-heading">
      <h1 id="unauthorized-heading" class="unauthorized__heading">Access denied</h1>
      <p class="unauthorized__message">
        You do not have permission to view this page.
      </p>
      <a routerLink="/movies" class="unauthorized__link">Back to the catalog</a>
    </section>
  `,
  styles: [
    `
      .unauthorized {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 0.75rem;
        padding: 3rem 1rem;
        text-align: center;
      }
      .unauthorized__heading {
        margin: 0;
      }
      .unauthorized__link {
        text-decoration: underline;
      }
    `,
  ],
})
export class UnauthorizedComponent {}
