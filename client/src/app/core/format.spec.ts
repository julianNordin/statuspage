import { describe, expect, it } from 'vitest';
import { formatMoment, formatRelative, formatUptime } from './format';

describe('formatUptime', () => {
  it('shows two decimals, because that is where the arguments happen', () => {
    expect(formatUptime(0.99989)).toBe('99.99%');
    expect(formatUptime(0.999)).toBe('99.90%');
    expect(formatUptime(1)).toBe('100.00%');
  });

  it('shows a dash rather than a number when there is no figure', () => {
    // A component nobody measured has not earned a percentage, and 0% would be a lie in the
    // other direction.
    expect(formatUptime(null)).toBe('—');
    expect(formatUptime(undefined)).toBe('—');
  });
});

describe('formatRelative', () => {
  const now = new Date('2026-08-18T19:30:00Z');

  it('reads as a person would say it', () => {
    expect(formatRelative('2026-08-18T19:29:57Z', now)).toBe('just now');
    expect(formatRelative('2026-08-18T19:29:00Z', now)).toBe('a minute ago');
    expect(formatRelative('2026-08-18T19:00:00Z', now)).toBe('30 minutes ago');
    expect(formatRelative('2026-08-18T16:30:00Z', now)).toBe('3 hours ago');
    expect(formatRelative('2026-08-17T19:30:00Z', now)).toBe('yesterday');
    expect(formatRelative('2026-08-14T19:30:00Z', now)).toBe('4 days ago');
  });

  it('does not say a negative time when a clock is ahead', () => {
    expect(formatRelative('2026-08-18T19:31:00Z', now)).toBe('just now');
  });

  it('says never rather than Invalid Date', () => {
    expect(formatRelative(null, now)).toBe('never');
    expect(formatRelative('not a date', now)).toBe('never');
  });
});

describe('formatMoment', () => {
  it('is unambiguous about the month', () => {
    // 08/09 is two different days depending on which side of the Atlantic you read it.
    expect(formatMoment('2026-08-18T19:30:00Z')).toContain('Aug');
    expect(formatMoment('2026-08-18T19:30:00Z')).toContain('2026');
  });

  it('shows a dash for nothing', () => {
    expect(formatMoment(null)).toBe('—');
    expect(formatMoment('nonsense')).toBe('—');
  });
});
