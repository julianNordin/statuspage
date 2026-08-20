import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { StatusService, type SnapshotState } from '../core/status.service';
import type { SnapshotComponent, StatusSnapshot } from '../core/status.models';
import { StatusPage } from './status-page';

function component(overrides: Partial<SnapshotComponent> = {}): SnapshotComponent {
  return {
    slug: 'api',
    name: 'API',
    state: 'Up',
    since: '2026-08-16T09:00:00Z',
    lastLatencyMs: 143,
    uptime: 0.9998,
    measuredHours: 2160,
    days: [
      { date: '2026-08-17', uptime: 1, worst: 'Up' },
      { date: '2026-08-18', uptime: null, worst: 'Unknown' },
    ],
    ...overrides,
  };
}

function snapshot(overrides: Partial<StatusSnapshot> = {}): StatusSnapshot {
  return {
    generatedAt: new Date().toISOString(),
    overall: 'Up',
    components: [component()],
    incidents: [],
    maintenance: [],
    ...overrides,
  };
}

/** A service whose state the test sets directly. The fetch itself is tested separately. */
class StubStatusService {
  readonly state = signal<SnapshotState>({ kind: 'loading' });
  readonly current = this.state.asReadonly();
  loaded = 0;

  async load(): Promise<void> {
    this.loaded++;
  }
}

describe('StatusPage', () => {
  let service: StubStatusService;

  beforeEach(async () => {
    service = new StubStatusService();

    await TestBed.configureTestingModule({
      imports: [StatusPage],
      providers: [
        provideZonelessChangeDetection(),
        { provide: StatusService, useValue: service },
      ],
    }).compileComponents();
  });

  async function render() {
    const fixture = TestBed.createComponent(StatusPage);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('asks for the snapshot when it opens', async () => {
    await render();

    expect(service.loaded).toBe(1);
  });

  it('says it is loading before anything has arrived', async () => {
    const dom = await render();

    expect(dom.textContent).toContain('Loading');
  });

  it('names the overall state in words, not only in colour', async () => {
    service.state.set({ kind: 'ready', snapshot: snapshot(), fetchedAt: new Date() });

    const dom = await render();

    expect(dom.textContent).toContain('All systems operational');
    expect(dom.textContent).toContain('API');
    expect(dom.textContent).toContain('99.98%');
  });

  it('says an outage is an outage', async () => {
    service.state.set({
      kind: 'ready',
      snapshot: snapshot({ overall: 'Down', components: [component({ state: 'Down' })] }),
      fetchedAt: new Date(),
    });

    const dom = await render();

    expect(dom.textContent).toContain('Some systems are down');
    expect(dom.textContent).toContain('Outage');
  });

  it('shows a dash rather than a percentage for a component nobody measured', async () => {
    service.state.set({
      kind: 'ready',
      snapshot: snapshot({
        overall: 'Unknown',
        components: [
          component({
            state: 'Unknown',
            uptime: null,
            measuredHours: 0,
            // A component with nothing measured has no good days either. A fixture where it
            // did would be testing a state the checker cannot produce.
            days: [{ date: '2026-08-18', uptime: null, worst: 'Unknown' }],
          }),
        ],
      }),
      fetchedAt: new Date(),
    });

    const dom = await render();

    expect(dom.textContent).toContain('—');
    expect(dom.textContent).toContain('not measured yet');
    expect(dom.textContent).not.toContain('100.00%');
  });

  it('says plainly that it could not load, rather than showing a reassuring blank', async () => {
    // The worst thing a status page can do is look fine because it failed to find out
    // otherwise.
    service.state.set({ kind: 'failed', reason: 'The current status could not be loaded.' });

    const dom = await render();

    const alert = dom.querySelector('[role="alert"]');
    expect(alert).not.toBeNull();
    expect(alert?.textContent).toContain('could not be loaded');
    expect(dom.textContent).not.toContain('All systems operational');
  });

  it('lists an open incident with what was said about it', async () => {
    service.state.set({
      kind: 'ready',
      snapshot: snapshot({
        overall: 'Down',
        incidents: [
          {
            id: 'i1',
            title: 'Elevated error rates',
            status: 'Investigating',
            impact: 'Major',
            startedAt: new Date().toISOString(),
            resolvedAt: null,
            affectedComponents: ['api'],
            updates: [
              {
                body: 'We are looking into it.',
                status: 'Investigating',
                postedAt: new Date().toISOString(),
                postedBy: 'Sam Operator',
              },
            ],
          },
        ],
      }),
      fetchedAt: new Date(),
    });

    const dom = await render();

    expect(dom.textContent).toContain('Open incidents');
    expect(dom.textContent).toContain('Elevated error rates');
    expect(dom.textContent).toContain('We are looking into it.');
    expect(dom.textContent).toContain('Sam Operator');
  });

  it('separates resolved incidents from open ones', async () => {
    service.state.set({
      kind: 'ready',
      snapshot: snapshot({
        incidents: [
          {
            id: 'i2',
            title: 'Old outage',
            status: 'Resolved',
            impact: 'Minor',
            startedAt: '2026-08-10T09:00:00Z',
            resolvedAt: '2026-08-10T10:00:00Z',
            affectedComponents: ['api'],
            updates: [],
          },
        ],
      }),
      fetchedAt: new Date(),
    });

    const dom = await render();

    expect(dom.textContent).toContain('Past incidents');
    expect(dom.textContent).not.toContain('Open incidents');
  });

  it('gives every day of history a label a screen reader can read', async () => {
    service.state.set({ kind: 'ready', snapshot: snapshot(), fetchedAt: new Date() });

    const dom = await render();
    const bars = dom.querySelectorAll('.bar');

    expect(bars.length).toBe(2);
    expect(bars[0].textContent).toContain('available');

    // The unmeasured day says so rather than being drawn as a good one.
    expect(bars[1].textContent).toContain('not measured');
  });
});
