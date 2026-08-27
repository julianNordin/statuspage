import { expect, test } from '@playwright/test';
import { ensureComponent } from './support';

test.describe('the public status page', () => {
  test('shows the overall state in words', async ({ page }) => {
    await page.goto('/');

    // Never colour alone. Whatever the state is, it is said. Anchored and level-scoped: a
    // loose regex here also matched a component happening to be named "down-<timestamp>".
    await expect(page.locator('.overall__headline')).toHaveText(
      /^(All systems operational|Some systems degraded|Some systems are down|Nothing measured yet)$/,
    );
  });

  test('lists a component with its history', async ({ page, request }) => {
    const slug = await ensureComponent(request, `e2e-${Date.now()}`);
    await page.goto('/');

    const component = page.getByRole('listitem').filter({ hasText: slug });
    await expect(component).toBeVisible();

    // Ninety bars, each carrying its own label rather than relying on the colour.
    await expect(component.locator('.bar')).toHaveCount(90);
  });

  test('says when it last heard anything', async ({ page }) => {
    await page.goto('/');

    await expect(page.getByText(/Last checked/)).toBeVisible();
  });

  test('explains what it is', async ({ page }) => {
    await page.goto('/');

    await expect(
      page.getByText(/does not call the API it reports on/),
    ).toBeVisible();
  });
});
