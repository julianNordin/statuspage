import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Keeps the console behind a sign-in.
 *
 * This is a redirect, not a security boundary. Every endpoint it protects is protected on the
 * server by a fallback authorization policy, and a guard that ran on the client would be
 * enforced by nothing at all — anybody can edit what a browser does. It exists so that an
 * operator whose token expired sees a sign-in form instead of a console where every request
 * comes back 401.
 */
export const requireOperator: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isSignedIn()) {
    return true;
  }

  return router.createUrlTree(['/sign-in'], {
    queryParams: { next: state.url },
  });
};
