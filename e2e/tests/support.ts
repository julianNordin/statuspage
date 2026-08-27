import { expect, type APIRequestContext, type Page } from '@playwright/test';

export const API = 'http://localhost:5080/api';
export const SNAPSHOT = 'http://127.0.0.1:10000/devstoreaccount1/status/status.json';

export const OPERATOR = {
  email: 'operator@example.com',
  password: 'Statuspage-Demo-1',
};

/** A bearer token for the seeded operator, for tests that need to arrange state. */
export async function tokenFor(request: APIRequestContext): Promise<string> {
  const response = await request.post(`${API}/auth/token`, { data: OPERATOR });
  expect(response.ok(), 'the seeded operator should be able to sign in').toBeTruthy();
  return (await response.json()).accessToken;
}

/** Adds a component, returning its slug. Ignores a slug that already exists. */
export async function ensureComponent(
  request: APIRequestContext,
  slug: string,
  targetUrl = 'https://example.com/',
): Promise<string> {
  const token = await tokenFor(request);

  await request.post(`${API}/components`, {
    headers: { Authorization: `Bearer ${token}` },
    data: {
      name: slug,
      slug,
      targetUrl,
      expectedStatusCode: 200,
      degradedAboveMs: 2000,
      failuresToOpen: 2,
      successesToClose: 2,
      enabled: true,
      position: 0,
    },
  });

  // The checker publishes the snapshot on its own schedule. Rebuilding it here means the
  // browser sees the change without the suite waiting out a cycle.
  await request.post(`${API}/read-model/rebuild`, {
    headers: { Authorization: `Bearer ${token}` },
  });

  return slug;
}

/** Signs in through the form, the way an operator does. */
export async function signIn(page: Page): Promise<void> {
  await page.goto('/sign-in');
  await page.getByLabel('Email').fill(OPERATOR.email);
  await page.getByLabel('Password').fill(OPERATOR.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/admin/);
}
