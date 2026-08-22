import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';
import { RUNTIME_CONFIG } from '../core/runtime-config';

/**
 * Attaches the bearer token to API calls, and to nothing else.
 *
 * The scoping is the point. The public page fetches its snapshot from blob storage, and a
 * token attached to that request would be an operator credential sent to a storage account
 * that has no use for it and every opportunity to log it.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const config = inject(RUNTIME_CONFIG);

  const token = auth.token();
  if (!token || !request.url.startsWith(config.apiUrl)) {
    return next(request);
  }

  return next(
    request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }),
  );
};
