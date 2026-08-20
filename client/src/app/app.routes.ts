import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Service status',
    loadComponent: () => import('./status/status-page').then((m) => m.StatusPage),
  },
  { path: '**', redirectTo: '' },
];
