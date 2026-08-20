import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { RUNTIME_CONFIG } from './runtime-config';
import type { StatusSnapshot } from './status.models';

/** What the page knows right now. */
export type SnapshotState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly snapshot: StatusSnapshot; readonly fetchedAt: Date }
  | { readonly kind: 'failed'; readonly reason: string };

/**
 * Fetches the public snapshot.
 *
 * It reads a JSON file from blob storage. There is no API call on this path and there must
 * not be one: the whole reason the snapshot exists is that a status page has to keep working
 * when the system it reports on does not.
 */
@Injectable({ providedIn: 'root' })
export class StatusService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(RUNTIME_CONFIG);

  private readonly state = signal<SnapshotState>({ kind: 'loading' });

  /** The current snapshot, as a signal the template reads. */
  readonly current = this.state.asReadonly();

  async load(): Promise<void> {
    try {
      // cache: no-store would defeat the sixty-second Cache-Control the checker sets, which
      // is there so a link doing the rounds during an outage does not become the outage.
      const snapshot = await this.http
        .get<StatusSnapshot>(this.config.snapshotUrl)
        .toPromise();

      if (!snapshot) {
        this.state.set({ kind: 'failed', reason: 'The status file was empty.' });
        return;
      }

      this.state.set({ kind: 'ready', snapshot, fetchedAt: new Date() });
    } catch {
      // Deliberately vague. A reader who cannot reach the status page needs to know that,
      // not which header was missing.
      this.state.set({
        kind: 'failed',
        reason: 'The current status could not be loaded. Please try again shortly.',
      });
    }
  }
}
