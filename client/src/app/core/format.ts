/** Small formatting helpers, kept out of the templates so they can be tested. */

/** "99.98%", or an em dash when there is no figure to show. */
export function formatUptime(value: number | null | undefined): string {
  if (value === null || value === undefined) {
    return '—';
  }

  // Two decimals, because the difference between 99.9% and 99.99% is most of the argument
  // anyone ever has about a status page.
  return `${(value * 100).toFixed(2)}%`;
}

/** "4 minutes ago", for a timestamp a reader is checking for freshness. */
export function formatRelative(from: string | Date | null | undefined, now: Date = new Date()): string {
  if (!from) {
    return 'never';
  }

  const then = from instanceof Date ? from : new Date(from);
  if (Number.isNaN(then.getTime())) {
    return 'never';
  }

  const seconds = Math.round((now.getTime() - then.getTime()) / 1000);

  if (seconds < 0) {
    return 'just now';
  }
  if (seconds < 60) {
    return seconds < 10 ? 'just now' : `${seconds} seconds ago`;
  }

  const minutes = Math.round(seconds / 60);
  if (minutes < 60) {
    return minutes === 1 ? 'a minute ago' : `${minutes} minutes ago`;
  }

  const hours = Math.round(minutes / 60);
  if (hours < 24) {
    return hours === 1 ? 'an hour ago' : `${hours} hours ago`;
  }

  const days = Math.round(hours / 24);
  return days === 1 ? 'yesterday' : `${days} days ago`;
}

/** "18 Aug 2026, 19:30" — unambiguous, and not in American order. */
export function formatMoment(value: string | Date | null | undefined): string {
  if (!value) {
    return '—';
  }

  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '—';
  }

  return new Intl.DateTimeFormat('en-GB', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date);
}
