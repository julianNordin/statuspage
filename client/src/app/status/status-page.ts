import { ChangeDetectionStrategy, Component, computed, inject, OnInit } from '@angular/core';
import { formatMoment, formatRelative, formatUptime } from '../core/format';
import { StatusService } from '../core/status.service';
import {
  OVERALL_LABEL,
  STATE_LABEL,
  type ComponentState,
  type SnapshotIncident,
} from '../core/status.models';
import { StateBadge } from './state-badge';
import { UptimeBars } from './uptime-bars';

/**
 * The public status page.
 *
 * It reads a JSON file and calls no API. That is the design, not an optimisation: a status
 * page served by the system it reports on tells you nothing at the one moment anybody opens
 * it.
 */
@Component({
  selector: 'sp-status-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [StateBadge, UptimeBars],
  templateUrl: './status-page.html',
  styleUrl: './status-page.css',
})
export class StatusPage implements OnInit {
  private readonly status = inject(StatusService);

  readonly state = this.status.current;

  readonly snapshot = computed(() => {
    const current = this.state();
    return current.kind === 'ready' ? current.snapshot : null;
  });

  readonly overallLabel = computed<string>(() => {
    const snapshot = this.snapshot();
    return snapshot ? OVERALL_LABEL[snapshot.overall] : '';
  });

  readonly openIncidents = computed<readonly SnapshotIncident[]>(
    () => this.snapshot()?.incidents.filter((i) => i.status !== 'Resolved') ?? [],
  );

  readonly pastIncidents = computed<readonly SnapshotIncident[]>(
    () => this.snapshot()?.incidents.filter((i) => i.status === 'Resolved') ?? [],
  );

  async ngOnInit(): Promise<void> {
    await this.status.load();
  }

  uptime(value: number | null): string {
    return formatUptime(value);
  }

  relative(value: string | null): string {
    return formatRelative(value);
  }

  moment(value: string | null): string {
    return formatMoment(value);
  }

  stateLabel(state: ComponentState): string {
    return STATE_LABEL[state];
  }

  /** How much of the window was actually observed, in plain words. */
  measured(hours: number): string {
    if (hours <= 0) {
      return 'not measured yet';
    }

    const days = hours / 24;
    return days >= 1
      ? `measured over ${Math.round(days)} day${Math.round(days) === 1 ? '' : 's'}`
      : `measured over ${Math.round(hours)} hour${Math.round(hours) === 1 ? '' : 's'}`;
  }
}
