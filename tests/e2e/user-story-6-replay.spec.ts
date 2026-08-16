import { test, expect, openAndWait } from "./support/fixtures";
import { seedOrchestration } from "./support/api";

test.describe("User Story 6 - replay and audit", () => {
  test("lists a completed mission and opens its conversation replay", async ({ page, request, testSuffix }) => {
    const missionId = `e2e-us6-${testSuffix}`;
    const messageId = `${missionId}-message`;
    await seedOrchestration(request, {
      mission: {
        missionId,
        goal: "E2E: replay a completed mission",
        status: "Succeeded",
        outcome: "Succeeded",
        stopReason: "StopConditionMet",
        budget: { costUsedUsd: 0.34, iterationsUsed: 1 },
      },
      messages: [{ messageId, missionId, kind: "Report", body: "完了した成果物と検証結果を記録しました。" }],
      artifacts: [{ artifactId: `${missionId}-artifact`, missionId, sourceMessageId: messageId, path: "feature-result.md", summary: "completed result", contentHash: "hash" }],
    });

    await openAndWait(page, "/replay", "Replay & Audit");
    const row = page.getByTestId("completed-mission").filter({ hasText: "E2E: replay a completed mission" });
    await expect(row).toContainText("成功");
    await expect(row).toContainText("$0.34");
    await row.getByRole("link", { name: "再生", exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`/missions/${missionId}$`));
    await expect(page.getByTestId("message-thread")).toContainText("完了した成果物と検証結果");
  });
});
