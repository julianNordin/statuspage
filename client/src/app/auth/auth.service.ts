import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { RUNTIME_CONFIG } from '../core/runtime-config';

interface AccessTokenResponse {
  readonly accessToken: string;
  readonly expiresAt: string;
  readonly displayName: string;
}

interface StoredSession {
  readonly token: string;
  readonly expiresAt: string;
  readonly displayName: string;
}

const STORAGE_KEY = 'statuspage.session';

/**
 * Who is signed in, and the token that proves it.
 *
 * The token is kept in sessionStorage rather than localStorage: an operator console is used
 * in sittings, and a token that outlives the tab is a token nobody remembers leaving behind.
 * There is no refresh token — signing in again is a smaller cost than a rotation scheme with
 * nothing to justify it.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(RUNTIME_CONFIG);

  private readonly session = signal<StoredSession | null>(this.restore());

  readonly displayName = computed(() => this.session()?.displayName ?? null);

  readonly isSignedIn = computed(() => {
    const current = this.session();
    if (!current) {
      return false;
    }

    // An expired token is not a session. Checking here means the guard turns somebody away at
    // the door rather than letting them into a console where every request fails.
    return new Date(current.expiresAt).getTime() > Date.now();
  });

  /** The bearer token, or null when there is nothing to send. */
  token(): string | null {
    return this.isSignedIn() ? this.session()!.token : null;
  }

  async signIn(email: string, password: string): Promise<boolean> {
    try {
      const response = await firstValueFrom(
        this.http.post<AccessTokenResponse>(`${this.config.apiUrl}/auth/token`, {
          email,
          password,
        }),
      );

      const session: StoredSession = {
        token: response.accessToken,
        expiresAt: response.expiresAt,
        displayName: response.displayName,
      };

      this.session.set(session);
      this.persist(session);
      return true;
    } catch {
      // The API answers a wrong password and an unknown address identically, and so does
      // this. Telling them apart here would undo that on the client.
      return false;
    }
  }

  signOut(): void {
    this.session.set(null);
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // Storage can be unavailable — a private window, or a browser configured to refuse it.
      // Signing out of the in-memory session is the part that matters.
    }
  }

  private persist(session: StoredSession): void {
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    } catch {
      // The console still works for this tab; it just will not survive a reload.
    }
  }

  private restore(): StoredSession | null {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      return raw ? (JSON.parse(raw) as StoredSession) : null;
    } catch {
      return null;
    }
  }
}
