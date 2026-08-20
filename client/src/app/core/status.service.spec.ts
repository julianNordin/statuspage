import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { RUNTIME_CONFIG } from './runtime-config';
import { StatusService } from './status.service';
import type { StatusSnapshot } from './status.models';

const SNAPSHOT_URL = 'https://example.blob.core.windows.net/status/status.json';

const SAMPLE: StatusSnapshot = {
  generatedAt: '2026-08-18T19:30:00Z',
  overall: 'Up',
  components: [],
  incidents: [],
  maintenance: [],
};

describe('StatusService', () => {
  let http: HttpTestingController;
  let service: StatusService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: RUNTIME_CONFIG,
          useValue: { snapshotUrl: SNAPSHOT_URL, apiUrl: 'https://example.test/api' },
        },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(StatusService);
  });

  afterEach(() => http.verify());

  it('reads the snapshot from blob storage and never calls the API', async () => {
    // The point of the whole read model. If this ever becomes a call to /api/status, the
    // page stops working at exactly the moment somebody opens it.
    const loading = service.load();

    const request = http.expectOne(SNAPSHOT_URL);
    expect(request.request.method).toBe('GET');
    request.flush(SAMPLE);

    await loading;

    const state = service.current();
    expect(state.kind).toBe('ready');
    expect(state.kind === 'ready' && state.snapshot.overall).toBe('Up');
  });

  it('starts out loading', () => {
    expect(service.current().kind).toBe('loading');
  });

  it('reports a failure rather than pretending everything is fine', async () => {
    const loading = service.load();

    http.expectOne(SNAPSHOT_URL).flush('nope', { status: 503, statusText: 'Unavailable' });

    await loading;

    const state = service.current();
    expect(state.kind).toBe('failed');
    expect(state.kind === 'failed' && state.reason).toContain('could not be loaded');
  });

  it('does not put the server error text in front of a reader', async () => {
    const loading = service.load();

    http
      .expectOne(SNAPSHOT_URL)
      .flush('BlobNotFound: the container does not exist', { status: 404, statusText: 'Not Found' });

    await loading;

    const state = service.current();
    expect(state.kind).toBe('failed');
    expect(state.kind === 'failed' && state.reason).not.toContain('BlobNotFound');
  });
});
