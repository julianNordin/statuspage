import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from './auth.service';

@Component({
  selector: 'sp-sign-in-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './sign-in-page.html',
  styleUrl: './sign-in-page.css',
})
export class SignInPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);

  readonly submitting = signal(false);
  readonly failed = signal(false);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  async submit(): Promise<void> {
    this.failed.set(false);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    try {
      const { email, password } = this.form.getRawValue();
      const ok = await this.auth.signIn(email, password);

      if (!ok) {
        this.failed.set(true);
        return;
      }

      // Where they were headed before the guard sent them here. Validated as a path rather
      // than trusted: a next parameter that accepts an absolute URL is an open redirect.
      const next = this.route.snapshot.queryParamMap.get('next');
      await this.router.navigateByUrl(SignInPage.safeNext(next));
    } finally {
      this.submitting.set(false);
    }
  }

  /** Only same-site paths. Anything else goes to the console. */
  static safeNext(next: string | null): string {
    if (!next || !next.startsWith('/') || next.startsWith('//')) {
      return '/admin';
    }

    return next;
  }

  invalid(field: 'email' | 'password'): boolean {
    const control = this.form.controls[field];
    return control.invalid && (control.dirty || control.touched);
  }
}
