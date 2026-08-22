import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError, ApiService } from '../core/api.service';
import type { ComponentResponse } from '../core/api.models';

@Component({
  selector: 'sp-components-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  templateUrl: './components-page.html',
  styleUrl: './admin.css',
})
export class ComponentsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly fb = inject(FormBuilder);

  readonly components = signal<readonly ComponentResponse[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(80)]],
    slug: ['', [Validators.required, Validators.pattern(/^[a-z0-9]+(-[a-z0-9]+)*$/)]],
    targetUrl: ['', [Validators.required]],
    expectedStatusCode: [200, [Validators.required, Validators.min(100), Validators.max(599)]],
    degradedAboveMs: [500, [Validators.required, Validators.min(0)]],
    failuresToOpen: [3, [Validators.required, Validators.min(1)]],
    successesToClose: [2, [Validators.required, Validators.min(1)]],
  });

  async ngOnInit(): Promise<void> {
    await this.refresh();
  }

  async refresh(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.components.set(await this.api.listComponents());
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : 'Could not load components.');
    } finally {
      this.loading.set(false);
    }
  }

  async add(): Promise<void> {
    this.error.set(null);
    this.fieldErrors.set({});

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    try {
      await this.api.createComponent({ ...this.form.getRawValue(), enabled: true, position: 0 });
      this.form.reset({
        name: '',
        slug: '',
        targetUrl: '',
        expectedStatusCode: 200,
        degradedAboveMs: 500,
        failuresToOpen: 3,
        successesToClose: 2,
      });
      await this.refresh();
    } catch (e) {
      if (e instanceof ApiError) {
        // The server's own words. "That address is not reachable from the public internet"
        // tells an operator what to change; "Bad Request" does not.
        this.error.set(e.message);
        this.fieldErrors.set(e.fieldErrors);
      } else {
        this.error.set('Could not add the component.');
      }
    } finally {
      this.saving.set(false);
    }
  }

  async toggle(component: ComponentResponse): Promise<void> {
    this.error.set(null);
    try {
      await this.api.updateComponent(component.id, {
        name: component.name,
        targetUrl: component.targetUrl,
        expectedStatusCode: component.expectedStatusCode,
        degradedAboveMs: component.degradedAboveMs,
        failuresToOpen: component.failuresToOpen,
        successesToClose: component.successesToClose,
        enabled: !component.enabled,
        position: component.position,
      });
      await this.refresh();
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : 'Could not update the component.');
    }
  }

  async remove(component: ComponentResponse): Promise<void> {
    this.error.set(null);
    try {
      await this.api.deleteComponent(component.id);
      await this.refresh();
    } catch (e) {
      this.error.set(e instanceof ApiError ? e.message : 'Could not delete the component.');
    }
  }

  invalid(field: string): boolean {
    const control = this.form.get(field);
    return !!control && control.invalid && (control.dirty || control.touched);
  }
}
