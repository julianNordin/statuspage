import { defineConfig, devices } from '@playwright/test';

/**
 * The browser suite runs against the Compose stack, not against mocks.
 *
 * `docker compose up -d` first: the API on 5080, Azurite on 10000, the checker writing a
 * snapshot every thirty seconds. The Angular dev server is started by Playwright and points
 * at both through client/public/config.json.
 *
 * That combination is the only place several of this project's claims can be checked at all.
 * The page reading a file rather than an API, the storage account allowing a cross-origin
 * read, an operator's change reaching the public page — none of them exist inside a single
 * process.
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list']],

  use: {
    baseURL: 'http://localhost:4200',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },

  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],

  webServer: {
    command: 'npm start -- --port 4200',
    cwd: '../client',
    url: 'http://localhost:4200',
    // Reuse whatever is already serving locally; in CI there is never one to reuse, and
    // silently attaching to a stale server is how a suite passes against last week's build.
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
    stdout: 'pipe',
    stderr: 'pipe',
  },
});
