import type { ComponentState, IncidentImpact, IncidentStatus } from './status.models';

export interface ComponentResponse {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly targetUrl: string;
  readonly expectedStatusCode: number;
  readonly degradedAboveMs: number;
  readonly failuresToOpen: number;
  readonly successesToClose: number;
  readonly enabled: boolean;
  readonly position: number;
}

export interface CreateComponentRequest {
  readonly name: string;
  readonly slug: string;
  readonly targetUrl: string;
  readonly expectedStatusCode: number;
  readonly degradedAboveMs: number;
  readonly failuresToOpen: number;
  readonly successesToClose: number;
  readonly enabled: boolean;
  readonly position: number;
}

export type UpdateComponentRequest = Omit<CreateComponentRequest, 'slug'>;

export interface IncidentUpdateResponse {
  readonly body: string;
  readonly status: IncidentStatus;
  readonly postedAt: string;
  readonly postedBy: string | null;
}

export interface IncidentResponse {
  readonly id: string;
  readonly title: string;
  readonly status: IncidentStatus;
  readonly impact: IncidentImpact;
  readonly startedAt: string;
  readonly resolvedAt: string | null;
  readonly openedAutomatically: boolean;
  readonly affectedComponents: readonly string[];
  readonly updates: readonly IncidentUpdateResponse[];
}

export interface DeclareIncidentRequest {
  readonly title: string;
  readonly body: string;
  readonly impact: IncidentImpact;
  readonly componentSlugs: readonly string[];
}

export interface PostIncidentUpdateRequest {
  readonly body: string;
  readonly status: IncidentStatus;
}

export interface StatusResponse {
  readonly generatedAt: string;
  readonly overall: ComponentState;
  readonly components: readonly {
    readonly slug: string;
    readonly name: string;
    readonly state: ComponentState;
    readonly since: string | null;
    readonly uptime: number | null;
    readonly measuredHours: number;
  }[];
}

/** The RFC 9457 shape every failure comes back in. */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}
