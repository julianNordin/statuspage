import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { formatMoment, formatRelative } from '../core/format';
import { ApiError, ApiService } from '../core/api.service';
import type { ComponentResponse, IncidentResponse } from '../core/api.models';
import type { IncidentStatus } from '../core/status.models';

@Component({
  selector: 'sp-incidents-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  templateUrl: './incidents-page.html',
  styleUrl: './admin.css',
})
export class IncidentsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly incidents = signal<readonly IncidentResponse[]>([]);
  readonly components = signal<readonly ComponentResponse[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  readonly open = computed(() => this.incidents().filter((i) => i.status !== 'Resolved'));
  readonly resolved = computed(() => this.incidents().filter((i) => i.status === 'Resolved'));

  /** Every status an open incident may be moved to. Resolved is final and the API refuses it. */
  readonly statuses: readonly IncidentStatus[] = [
    'Investigating',
    'Identified',
    'Monitoring',
    'Resolved',
  ];

  readonly declareForm = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(160)]],
    body: ['', [Validators.required, Validators.maxLength(4000)]],
    impact: ['Minor' as const, [Validators.required]],
    slug: ['', [Validators.required]],
  });

  readonly updateForm = this.fb.nonNullable.group({
    body: ['', [Validators.required, Validators.maxLength(4000)]],
    status: ['Investigating' as IncidentStatus, [Validators.required]],
  });

  readonly updating = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.refresh();
  }

  async refresh(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const [incidents, components] = await Promise.all([
        this.api.listIncidents(),
        this.api.listComponents(),
      ]);
      this.incidents.set(incidents);
      this.components.set(components);
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : 'Could not load incidents.');
    } finally {
      this.loading.set(false);
    }
  }

  async declare(): Promise<void> {
    this.error.set(null);

    if (this.declareForm.invalid) {
      this.declareForm.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    try {
      const { title, body, impact, slug } = this.declareForm.getRawValue();
      await this.api.declareIncident({ title, body, impact, componentSlugs: [slug] });
      this.declareForm.reset({ title: '', body: '', impact: 'Minor', slug: '' });
      await this.refresh();
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : 'Could not declare the incident.');
    } finally {
      this.saving.set(false);
    }
  }

  startUpdate(incident: IncidentResponse): void {
    this.updating.set(incident.id);
    this.updateForm.reset({ body: '', status: incident.status });
  }

  cancelUpdate(): void {
    this.updating.set(null);
  }

  async postUpdate(incident: IncidentResponse): Promise<void> {
    this.error.set(null);

    if (this.updateForm.invalid) {
      this.updateForm.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    try {
      await this.api.postIncidentUpdate(incident.id, this.updateForm.getRawValue());
      this.updating.set(null);
      await this.refresh();
    } catch (e) {
      // A refused transition comes back as a 409 whose detail explains that resolved is
      // final and what to do instead. Showing it verbatim is more use than any wording
      // invented here.
      this.error.set(e instanceof ApiError ? e.message : 'Could not post the update.');
    } finally {
      this.saving.set(false);
    }
  }

  relative(value: string | null): string {
    return formatRelative(value);
  }

  moment(value: string | null): string {
    return formatMoment(value);
  }
}
