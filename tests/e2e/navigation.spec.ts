import { test, expect, openAndWait } from "./support/fixtures";
import {
  assertNavigationTarget,
  mainNavigation,
  navigationTargets,
} from "./support/navigation";

for (const target of navigationTargets) {
  test(`navigates to ${target.conceptualName}`, async ({ page, ready }) => {
    await openAndWait(page, "/", "Mission Control");

    if (target.linkName) {
      await mainNavigation(page).getByRole("link", { name: target.linkName, exact: true }).click();
    } else {
      await page.goto(target.entryPath);
    }

    await ready(target.heading);
    await assertNavigationTarget(page, target);

    if (target.conceptualName === "Chat") {
      await expect(page.getByRole("button", { name: /Test Agent/i })).toBeVisible();
    }
  });
}
