import { test, expect, openAndWait } from "./support/fixtures";
import { seedOrchestration } from "./support/api";

test.describe("User Story 5 - unattended triggers", () => {
  test("lists a schedule, exposes overlap policy, and toggles enablement", async ({ page, request, testSuffix }) => {
    const triggerName = `e2e-trigger-${testSuffix}`;
    await seedOrchestration(request, {
      triggers: [{
        triggerId: `${triggerName}-id`,
        name: triggerName,
        kind: "Schedule",
        targetName: "demo-team",
        cron: "0 9 * * 1",
        overlapPolicy: "Queue",
        enabled: true,
      }],
    });

    await openAndWait(page, "/triggers", "Triggers");
    const row = page.getByTestId("trigger-row").filter({ hasText: triggerName });
    await expect(row).toContainText("cron 0 9 * * 1");
    await expect(row).toContainText("待機");
    const toggle = row.getByRole("button", { name: `${triggerName} を無効にする` });
    await toggle.click();
    await expect(row.getByRole("button", { name: `${triggerName} を有効にする` })).toBeVisible();
  });
});
