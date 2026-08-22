import { Routes } from '@angular/router';
import { requireOperator } from './auth/auth.guard';

export const routes: Routes = [
  {
    path: '',
    title: 'Service status',
    loadComponent: () => import('./status/status-page').then((m) => m.StatusPage),
  },
  {
    path: 'sign-in',
    title: 'Operator sign in',
    loadComponent: () => import('./auth/sign-in-page').then((m) => m.SignInPage),
  },
  {
    path: 'admin',
    // A redirect, not a boundary. Every endpoint behind it is protected on the server by a
    // fallback policy; this exists so an operator whose token expired sees a sign-in form
    // rather than a console where every request comes back 401.
    canActivate: [requireOperator],
    loadComponent: () => import('./admin/admin-shell').then((m) => m.AdminShell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'components' },
      {
        path: 'components',
        title: 'Components',
        loadComponent: () => import('./admin/components-page').then((m) => m.ComponentsPage),
      },
      {
        path: 'incidents',
        title: 'Incidents',
        loadComponent: () => import('./admin/incidents-page').then((m) => m.IncidentsPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
