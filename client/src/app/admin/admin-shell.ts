import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../auth/auth.service';

@Component({
  selector: 'sp-admin-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="shell">
      <header class="bar">
        <a class="brand" routerLink="/">Status</a>
        <nav class="nav" aria-label="Console">
          <a routerLink="/admin/components" routerLinkActive="active">Components</a>
          <a routerLink="/admin/incidents" routerLinkActive="active">Incidents</a>
        </nav>
        <div class="who">
          <span>{{ auth.displayName() }}</span>
          <button type="button" (click)="signOut()">Sign out</button>
        </div>
      </header>
      <main class="body"><router-outlet /></main>
    </div>
  `,
  styles: `
    .shell { min-height: 100dvh; }

    .bar {
      display: flex;
      align-items: center;
      gap: var(--space-5);
      padding: var(--space-3) var(--space-4);
      background: var(--paper);
      border-bottom: 1px solid var(--rule);
      flex-wrap: wrap;
    }

    .brand { font-weight: 640; text-decoration: none; color: var(--ink); }

    .nav { display: flex; gap: var(--space-4); margin-right: auto; }
    .nav a { color: var(--ink-muted); text-decoration: none; font-size: var(--text-sm); }
    .nav a.active { color: var(--accent); font-weight: 560; }

    .who { display: flex; align-items: center; gap: var(--space-3); font-size: var(--text-sm); }
    .who span { color: var(--ink-muted); }
    .who button {
      font: inherit;
      border: 1px solid var(--rule);
      background: var(--paper);
      color: var(--ink);
      border-radius: var(--radius-sm);
      padding: var(--space-1) var(--space-3);
      cursor: pointer;
    }

    .body { max-width: 60rem; margin: 0 auto; padding: var(--space-6) var(--space-4); }
  `,
})
export class AdminShell {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  async signOut(): Promise<void> {
    this.auth.signOut();
    await this.router.navigateByUrl('/');
  }
}
