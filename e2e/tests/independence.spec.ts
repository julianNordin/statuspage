import { execSync } from 'node:child_process';
import { expect, test } from '@playwright/test';
import { API, SNAPSHOT, ensureComponent } from './support';

/**
 * The claim the whole project is built around: the page that reports on the system does not
 * depend on the system.
 *
 * Two tests, because they fail for different reasons. The first catches somebody adding an
 * API call to the public path — the change that would break this quietly, on a day when
 * everything is up and nothing looks wrong. The second is the real thing: the API is stopped
 * and the page is asked to render anyway.
 */
test.describe('the public page does not depend on the API', () => {
  test('makes no request to the API at all', async ({ page }) => {
    const apiCalls: string[] = [];

    page.on('request', (request) => {
      if (request.url().startsWith(API)) {
        apiCalls.push(`${request.method()} ${request.url()}`);
      }
    });

    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    // Wait past anything deferred, so a lazily-issued call is still caught.
    await page.waitForTimeout(1500);

    expect(
      apiCalls,
      'the public page must read the snapshot and nothing else — an API call here is the ' +
        'bug this whole design exists to prevent',
    ).toEqual([]);
  });

  test('reads the snapshot from blob storage', async ({ page }) => {
    const snapshotReads: string[] = [];

    page.on('request', (request) => {
      if (request.url().startsWith(SNAPSHOT)) {
        snapshotReads.push(request.url());
      }
    });

    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await page.waitForTimeout(1000);

    expect(snapshotReads.length).toBeGreaterThan(0);
  });

  test('still renders with the API stopped', async ({ page, request }) => {
    test.slow();

    await ensureComponent(request, `down-${Date.now()}`);

    try {
      execSync('docker compose stop api', { cwd: '..', stdio: 'pipe' });

      // The API is gone. A page that called it would show an error, or nothing at all.
      await page.goto('/');

      await expect(page.locator('.overall__headline')).toHaveText(
        /^(All systems operational|Some systems degraded|Some systems are down|Nothing measured yet)$/,
      );

      await expect(page.getByRole('alert')).toHaveCount(0);
      await expect(page.getByText(/Last checked/)).toBeVisible();
    } finally {
      execSync('docker compose start api', { cwd: '..', stdio: 'pipe' });

      // Leave the stack as it was found, or every test after this one fails for the wrong
      // reason. The request throws while the container is still coming up — a refused
      // connection is not a status code — so the poll has to survive that rather than
      // report it as the outcome.
      await expect
        .poll(
          async () => {
            try {
              return (await request.get(`${API}/status`)).status();
            } catch {
              return 0;
            }
          },
          { timeout: 90_000, intervals: [1000] },
        )
        .toBe(200);
    }
  });
});
