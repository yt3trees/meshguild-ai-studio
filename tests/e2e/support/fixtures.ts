import { test as base, expect, type Page } from "@playwright/test";
import { uniqueSuffix } from "./test-data";

type E2eFixtures = {
  ready: (heading?: string) => Promise<void>;
  testSuffix: string;
};

export const test = base.extend<E2eFixtures>({
  ready: async ({ page }, use) => {
    await use(async (heading?: string) => waitForPageReady(page, heading));
  },
  testSuffix: async ({}, use, testInfo) => {
    await use(uniqueSuffix(testInfo));
  },
});

export { expect };

export async function waitForPageReady(page: Page, heading?: string): Promise<void> {
  await expect(page.locator('[data-pw="blazor-ready"]')).toBeAttached();
  if (heading) {
    await expect(page.getByRole("heading", { name: heading, exact: true })).toBeVisible();
  }
}

export async function openAndWait(page: Page, path: string, heading?: string): Promise<void> {
  await page.goto(path);
  await waitForPageReady(page, heading);
}
