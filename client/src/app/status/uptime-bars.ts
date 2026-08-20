import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { formatUptime } from '../core/format';
import { STATE_LABEL, type SnapshotDay } from '../core/status.models';

/**
 * Ninety days, one bar each.
 *
 * The bars are a list, not a picture: each carries its own accessible label, so the history
 * is readable by somebody who never sees the colours at all. A day nobody measured is drawn
 * hollow rather than green — claiming availability for time you did not watch is the one
 * dishonesty a status page can commit without anybody noticing.
 */
@Component({
  selector: 'sp-uptime-bars',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol class="bars" aria-label="Daily availability, oldest first">
      @for (day of days(); track day.date) {
        <li class="bar" [attr.data-state]="day.worst" [attr.title]="describe(day)">
          <span class="visually-hidden">{{ describe(day) }}</span>
        </li>
      }
    </ol>
  `,
  styles: `
    .bars {
      display: flex;
      gap: 2px;
      margin: 0;
      padding: 0;
      list-style: none;
      height: 2rem;
      align-items: stretch;
    }

    .bar {
      flex: 1 1 0;
      min-width: 2px;
      border-radius: 1px;
      background: var(--unknown-soft);
      border: 1px solid transparent;
    }

    .bar[data-state='Up'] { background: var(--up); }
    .bar[data-state='Degraded'] { background: var(--degraded); }
    .bar[data-state='Down'] { background: var(--down); }

    /* Never measured: hollow, so it cannot be mistaken for a good day. */
    .bar[data-state='Unknown'] {
      background: transparent;
      border-color: var(--rule);
    }

    @media (max-width: 40rem) {
      .bars { height: 1.5rem; }
    }
  `,
})
export class UptimeBars {
  readonly days = input.required<readonly SnapshotDay[]>();

  describe(day: SnapshotDay): string {
    const when = new Date(day.date).toLocaleDateString('en-GB', {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });

    return day.uptime === null
      ? `${when}: not measured`
      : `${when}: ${formatUptime(day.uptime)} available, ${STATE_LABEL[day.worst].toLowerCase()}`;
  }
}
