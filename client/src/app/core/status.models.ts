/**
 * The shape of status.json, mirroring the records the checker writes.
 *
 * These are hand-written rather than generated. The snapshot is the contract between two
 * halves of this project, and a contract worth having is one somebody had to type out.
 */

export type ComponentState = 'Unknown' | 'Up' | 'Degraded' | 'Down';

export type IncidentStatus = 'Investigating' | 'Identified' | 'Monitoring' | 'Resolved';

export type IncidentImpact = 'None' | 'Minor' | 'Major' | 'Critical';

export interface SnapshotDay {
  readonly date: string;
  /** Availability that day, or null when nothing was measured. */
  readonly uptime: number | null;
  readonly worst: ComponentState;
}

export interface SnapshotComponent {
  readonly slug: string;
  readonly name: string;
  readonly state: ComponentState;
  readonly since: string | null;
  readonly lastLatencyMs: number | null;
  /** Availability over the window, or null when nothing was accountable. */
  readonly uptime: number | null;
  readonly measuredHours: number;
  readonly days: readonly SnapshotDay[];
}

export interface SnapshotUpdate {
  readonly body: string;
  readonly status: IncidentStatus;
  readonly postedAt: string;
  readonly postedBy: string | null;
}

export interface SnapshotIncident {
  readonly id: string;
  readonly title: string;
  readonly status: IncidentStatus;
  readonly impact: IncidentImpact;
  readonly startedAt: string;
  readonly resolvedAt: string | null;
  readonly affectedComponents: readonly string[];
  readonly updates: readonly SnapshotUpdate[];
}

export interface SnapshotMaintenance {
  readonly title: string;
  readonly description: string | null;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly affectedComponents: readonly string[];
}

export interface StatusSnapshot {
  readonly generatedAt: string;
  readonly overall: ComponentState;
  readonly components: readonly SnapshotComponent[];
  readonly incidents: readonly SnapshotIncident[];
  readonly maintenance: readonly SnapshotMaintenance[];
}

/** What a reader is told, for each state. Never colour alone. */
export const STATE_LABEL: Readonly<Record<ComponentState, string>> = {
  Up: 'Operational',
  Degraded: 'Degraded performance',
  Down: 'Outage',
  Unknown: 'Not yet measured',
};

export const OVERALL_LABEL: Readonly<Record<ComponentState, string>> = {
  Up: 'All systems operational',
  Degraded: 'Some systems degraded',
  Down: 'Some systems are down',
  Unknown: 'Nothing measured yet',
};
