import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { RUNTIME_CONFIG } from '../core/runtime-config';
import { authInterceptor } from './auth.interceptor';
import { requireOperator } from './auth.guard';
import { AuthService } from './auth.service';
import { SignInPage } from './sign-in-page';

const API = 'https://example.test/api';
const SNAPSHOT = 'https://example.blob.core.windows.net/status/status.json';

const CONFIG = { snapshotUrl: SNAPSHOT, apiUrl: API };

function tomorrow(): string {
  return new Date(Date.now() + 3_600_000).toISOString();
}

function yesterday(): string {
  return new Date(Date.now() - 3_600_000).toISOString();
}

describe('AuthService', () => {
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: RUNTIME_CONFIG, useValue: CONFIG },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('starts signed out', () => {
    expect(auth.isSignedIn()).toBe(false);
    expect(auth.token()).toBeNull();
  });

  it('keeps the token when the credentials are accepted', async () => {
    const signingIn = auth.signIn('operator@example.test', 'Tests-Only-Operator-1');

    http.expectOne(`${API}/auth/token`).flush({
      accessToken: 'a.b.c',
      expiresAt: tomorrow(),
      displayName: 'Sam Operator',
    });

    expect(await signingIn).toBe(true);
    expect(auth.isSignedIn()).toBe(true);
    expect(auth.token()).toBe('a.b.c');
    expect(auth.displayName()).toBe('Sam Operator');
  });

  it('reports a refusal without saying which half was wrong', async () => {
    // The API answers a wrong password and an unknown address identically. Telling them
    // apart on the client would undo that.
    const signingIn = auth.signIn('nobody@example.test', 'wrong');

    http.expectOne(`${API}/auth/token`).flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(await signingIn).toBe(false);
    expect(auth.isSignedIn()).toBe(false);
  });

  it('treats an expired token as no session at all', async () => {
    const signingIn = auth.signIn('operator@example.test', 'x');

    http.expectOne(`${API}/auth/token`).flush({
      accessToken: 'stale',
      expiresAt: yesterday(),
      displayName: 'Sam Operator',
    });

    await signingIn;

    expect(auth.isSignedIn()).toBe(false);
    expect(auth.token()).toBeNull();
  });

  it('forgets everything on sign out', async () => {
    const signingIn = auth.signIn('operator@example.test', 'x');
    http.expectOne(`${API}/auth/token`).flush({
      accessToken: 'a.b.c',
      expiresAt: tomorrow(),
      displayName: 'Sam Operator',
    });
    await signingIn;

    auth.signOut();

    expect(auth.isSignedIn()).toBe(false);
    expect(sessionStorage.getItem('statuspage.session')).toBeNull();
  });
});

describe('authInterceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let auth: AuthService;

  beforeEach(async () => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: RUNTIME_CONFIG, useValue: CONFIG },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    auth = TestBed.inject(AuthService);

    const signingIn = auth.signIn('operator@example.test', 'x');
    http.expectOne(`${API}/auth/token`).flush({
      accessToken: 'a.b.c',
      expiresAt: tomorrow(),
      displayName: 'Sam Operator',
    });
    await signingIn;
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('sends the token to the API', () => {
    client.get(`${API}/components`).subscribe();

    const request = http.expectOne(`${API}/components`);
    expect(request.request.headers.get('Authorization')).toBe('Bearer a.b.c');
  });

  it('does not send the token to blob storage', () => {
    // The public snapshot lives in a storage account that has no use for an operator
    // credential and every opportunity to log it.
    client.get(SNAPSHOT).subscribe();

    const request = http.expectOne(SNAPSHOT);
    expect(request.request.headers.has('Authorization')).toBe(false);
  });

  it('sends nothing once signed out', () => {
    auth.signOut();
    client.get(`${API}/components`).subscribe();

    const request = http.expectOne(`${API}/components`);
    expect(request.request.headers.has('Authorization')).toBe(false);
  });
});

describe('requireOperator', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: RUNTIME_CONFIG, useValue: CONFIG },
      ],
    });
  });

  it('sends a signed-out visitor to sign in, remembering where they were going', () => {
    const result = TestBed.runInInjectionContext(() =>
      requireOperator({} as never, { url: '/admin/incidents' } as never),
    );

    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toContain(
      'next=%2Fadmin%2Fincidents',
    );
  });
});

describe('SignInPage.safeNext', () => {
  it('follows a same-site path', () => {
    expect(SignInPage.safeNext('/admin/components')).toBe('/admin/components');
  });

  it('refuses anything that leaves the site', () => {
    // A next parameter that accepts an absolute URL is an open redirect, and a sign-in form
    // is exactly where one is worth having.
    expect(SignInPage.safeNext('https://evil.example/')).toBe('/admin');
    expect(SignInPage.safeNext('//evil.example/')).toBe('/admin');
    expect(SignInPage.safeNext('javascript:alert(1)')).toBe('/admin');
    expect(SignInPage.safeNext(null)).toBe('/admin');
    expect(SignInPage.safeNext('')).toBe('/admin');
  });
});
