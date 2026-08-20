import { InjectionToken, inject, provideAppInitializer } from '@angular/core';

/** Where the page reads its data from. */
export interface RuntimeConfig {
  /**
   * The full URL of status.json in blob storage.
   *
   * The page fetches this and nothing else — not the API, not the database it reports on. A
   * status page served by the system it describes tells you nothing at the one moment you
   * need it to.
   */
  readonly snapshotUrl: string;

  /** Base URL of the API. Used by the operator console only; the public page never calls it. */
  readonly apiUrl: string;
}

export const RUNTIME_CONFIG = new InjectionToken<RuntimeConfig>('RUNTIME_CONFIG');

const FALLBACK: RuntimeConfig = {
  snapshotUrl: '/status.json',
  apiUrl: '/api',
};

let loaded: RuntimeConfig = FALLBACK;

/**
 * Reads config.json at startup rather than baking the URLs into the bundle.
 *
 * One build artefact, configured where it is deployed. The alternative — a bundle per
 * environment — means the thing tested is not the thing shipped.
 */
export function provideRuntimeConfig() {
  return [
    provideAppInitializer(async () => {
      try {
        const response = await fetch('config.json', { cache: 'no-store' });
        if (response.ok) {
          loaded = { ...FALLBACK, ...(await response.json()) };
        }
      } catch {
        // A missing or malformed config.json leaves the same-origin defaults in place, which
        // is what the local dev server serves. Failing to start here would turn a
        // configuration slip into a blank page with nothing to read.
      }
    }),
    { provide: RUNTIME_CONFIG, useFactory: () => loaded },
  ];
}

/** Convenience for components that only need the config. */
export function runtimeConfig(): RuntimeConfig {
  return inject(RUNTIME_CONFIG);
}
