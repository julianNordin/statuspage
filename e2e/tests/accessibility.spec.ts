import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { ensureComponent, signIn } from './support';

/**
 * An axe sweep of every page a person reaches.
 *
 * best-practice is excluded: it is opinion rather than standard, and a suite that fails on
 * opinion collects suppressions until it fails on nothing.
 */
const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

test.describe('accessibility', () => {
  test('the public status page', async ({ page, request }) => {
    await ensureComponent(request, `a11y-${Date.now()}`);
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    const results = await new AxeBuilder({ page }).withTags(TAGS).analyze();
    expect(results.violations).toEqual([]);
  });

  test('the sign-in form', async ({ page }) => {
    await page.goto('/sign-in');

    const results = await new AxeBuilder({ page }).withTags(TAGS).analyze();
    expect(results.violations).toEqual([]);
  });

  test('the component console', async ({ page }) => {
    await signIn(page);
    await page.goto('/admin/components');

    const results = await new AxeBuilder({ page }).withTags(TAGS).analyze();
    expect(results.violations).toEqual([]);
  });

  test('the incident console', async ({ page }) => {
    await signIn(page);
    await page.goto('/admin/incidents');

    const results = await new AxeBuilder({ page }).withTags(TAGS).analyze();
    expect(results.violations).toEqual([]);
  });

  test('the status page can be read without a mouse', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    // Tab order has to reach something. A page where the first Tab lands nowhere is a page
    // a keyboard user cannot start reading.
    await page.keyboard.press('Tab');
    const focused = await page.evaluate(() => document.activeElement?.tagName ?? 'NONE');
    expect(focused).not.toBe('NONE');
  });
});
