import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { STATE_LABEL, type ComponentState } from '../core/status.models';

/**
 * A component's state, said in words as well as in colour.
 *
 * The dot is decorative and the text is the message. Roughly one man in twelve cannot
 * distinguish the red from the green, and a status page that encodes the only thing it
 * exists to say in hue alone is unreadable to them.
 */
@Component({
  selector: 'sp-state-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="badge" [attr.data-state]="state()">
      <span class="dot" aria-hidden="true"></span>
      <span class="label">{{ label() }}</span>
    </span>
  `,
  styles: `
    .badge {
      display: inline-flex;
      align-items: center;
      gap: var(--space-2);
      font-size: var(--text-sm);
      font-weight: 560;
      white-space: nowrap;
    }

    .dot {
      width: 0.6rem;
      height: 0.6rem;
      border-radius: 50%;
      flex: none;
    }

    .badge[data-state='Up'] { color: var(--up); }
    .badge[data-state='Up'] .dot { background: var(--up); }
    .badge[data-state='Degraded'] { color: var(--degraded); }
    .badge[data-state='Degraded'] .dot { background: var(--degraded); }
    .badge[data-state='Down'] { color: var(--down); }
    .badge[data-state='Down'] .dot { background: var(--down); }
    .badge[data-state='Unknown'] { color: var(--ink-faint); }
    .badge[data-state='Unknown'] .dot { background: var(--unknown); }
  `,
})
export class StateBadge {
  readonly state = input.required<ComponentState>();

  label(): string {
    return STATE_LABEL[this.state()];
  }
}
