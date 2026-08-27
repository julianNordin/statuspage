import { expect, test } from '@playwright/test';
import { API, OPERATOR, signIn, tokenFor } from './support';

test.describe('the operator console', () => {
  test('turns away somebody who is not signed in', async ({ page }) => {
    await page.goto('/admin/components');

    // A redirect, not a boundary — the endpoints behind it are closed on the server. What
    // this proves is that an operator sees a form rather than a console full of 401s.
    await expect(page).toHaveURL(/\/sign-in/);
    await expect(page.getByRole('heading', { name: 'Operator sign in' })).toBeVisible();
  });

  test('says so when the credentials are wrong', async ({ page }) => {
    await page.goto('/sign-in');
    await page.getByLabel('Email').fill(OPERATOR.email);
    await page.getByLabel('Password').fill('Not-The-Password-1');
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page.getByRole('alert')).toContainText('not accepted');
  });

  test('signs in and lands where the guard was heading', async ({ page }) => {
    await page.goto('/admin/incidents');
    await expect(page).toHaveURL(/next=/);

    await page.getByLabel('Email').fill(OPERATOR.email);
    await page.getByLabel('Password').fill(OPERATOR.password);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page).toHaveURL(/\/admin\/incidents/);
  });

  test('adds a component and shows it in the table', async ({ page }) => {
    const slug = `ui-${Date.now()}`;
    await signIn(page);

    await page.goto('/admin/components');
    await page.getByLabel('Name').fill(slug);
    await page.getByLabel('Slug').fill(slug);
    await page.getByLabel('Target URL').fill('https://example.com/');
    await page.getByRole('button', { name: 'Add component' }).click();

    // The name and the slug are both this string, so two cells match. Either proves the row
    // is there.
    await expect(page.getByRole('cell', { name: slug, exact: true }).first()).toBeVisible();
  });

  test('shows the server refusal for a target it must never fetch', async ({ page }) => {
    // The rule lives on the server and the console does not restate it. What arrives is what
    // the operator reads.
    await signIn(page);

    await page.goto('/admin/components');
    await page.getByLabel('Name').fill('metadata');
    await page.getByLabel('Slug').fill(`bad-${Date.now()}`);
    await page.getByLabel('Target URL').fill('http://169.254.169.254/metadata/identity/oauth2/token');
    await page.getByRole('button', { name: 'Add component' }).click();

    await expect(page.getByRole('alert')).toContainText(/not allowed|not reachable/i);
  });

  test('declares an incident that reaches the public page', async ({ page, request }) => {
    const slug = `inc-${Date.now()}`;

    // Unique per run. The suite writes to a database that persists between runs, so a fixed
    // title matches every incident a previous run left behind.
    const title = `Something is wrong ${slug}`;
    const body = `We are looking into it (${slug}).`;
    const token = await tokenFor(request);

    await request.post(`${API}/components`, {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        name: slug, slug, targetUrl: 'https://example.com/',
        expectedStatusCode: 200, degradedAboveMs: 2000,
        failuresToOpen: 2, successesToClose: 2, enabled: true, position: 0,
      },
    });

    await signIn(page);
    await page.goto('/admin/incidents');

    await page.getByLabel('Title').fill(title);
    await page.getByLabel('Component').selectOption(slug);
    await page.getByLabel('First update').fill(body);
    await page.getByRole('button', { name: 'Declare incident' }).click();

    await expect(page.getByRole('heading', { name: title })).toBeVisible();

    // The public page reads a snapshot, so the incident is there once it has been rebuilt.
    await request.post(`${API}/read-model/rebuild`, {
      headers: { Authorization: `Bearer ${token}` },
    });

    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Open incidents' })).toBeVisible();
    await expect(page.getByText(body)).toBeVisible();
  });

  test('signs out back to the public page', async ({ page }) => {
    await signIn(page);
    await page.getByRole('button', { name: 'Sign out' }).click();

    await expect(page).toHaveURL('http://localhost:4200/');

    await page.goto('/admin/components');
    await expect(page).toHaveURL(/\/sign-in/);
  });
});
