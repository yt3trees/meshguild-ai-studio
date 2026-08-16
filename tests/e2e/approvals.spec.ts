import { test, expect, openAndWait } from "./support/fixtures";
import { createApproval, getApproval } from "./support/api";
import { approvalFixture } from "./support/test-data";

test.describe("Approvals", () => {
  test("lists and approves a pending request", async ({ page, request, ready }, testInfo) => {
    const fixture = approvalFixture(testInfo, "approve");
    const seeded = await createApproval(request, fixture);

    await openAndWait(page, "/approvals", "Approvals");
    const row = page.getByTestId("approval-row").filter({ hasText: fixture.tool });
    await expect(row).toBeVisible();
    await row.click();
    await ready("Approvals");
    await expect(page.getByRole("region", { name: "承認要求の詳細" })).toContainText(fixture.tool);
    await page.getByRole("button", { name: "Approve & resume", exact: true }).click();

    await expect(row).toHaveCount(0);
    await expect.poll(async () => (await getApproval(request, seeded.approvalId)).status).toBe("Approved");
  });

  test("lists and rejects a pending request", async ({ page, request, ready }, testInfo) => {
    const fixture = approvalFixture(testInfo, "reject");
    const seeded = await createApproval(request, fixture);

    await openAndWait(page, "/approvals", "Approvals");
    const row = page.getByTestId("approval-row").filter({ hasText: fixture.tool });
    await expect(row).toBeVisible();
    await row.click();
    await ready("Approvals");
    await page.getByRole("button", { name: "Reject", exact: true }).click();

    await expect(row).toHaveCount(0);
    await expect.poll(async () => (await getApproval(request, seeded.approvalId)).status).toBe("Rejected");
  });
});
