import { test, expect, openAndWait } from "./support/fixtures";
import { seedOrchestration } from "./support/api";

test.describe("User Story 3 - loop engineering", () => {
  test("shows iteration scores, blocking metrics, and supports a controlled break", async ({ page, request, testSuffix }) => {
    const missionId = `e2e-us3-${testSuffix}`;
    await seedOrchestration(request, {
      mission: { missionId, goal: "E2E: converge a failing implementation", status: "Running" },
      loops: [{
        loopRunId: `${missionId}-loop`,
        missionId,
        nodeRunId: "review-and-converge",
        maxIterations: 3,
        costLimitUsd: 5,
        scoreThreshold: 0.95,
        iterations: [
          {
            iterationId: `${missionId}-it-1`,
            iterationNo: 1,
            state: "Failed",
            outputJson: "テスト 6 件失敗",
            costUsd: 0.58,
            durationMs: 362000,
            evaluation: {
              evaluationId: `${missionId}-eval-1`,
              score: 0.62,
              metrics: [{ metricId: `${missionId}-metric-1`, name: "tests", value: 14, target: 20, achieved: false }],
            },
          },
          {
            iterationId: `${missionId}-it-2`,
            iterationNo: 2,
            state: "Failed",
            outputJson: "テスト 2 件失敗",
            costUsd: 0.64,
            durationMs: 461000,
            evaluation: {
              evaluationId: `${missionId}-eval-2`,
              score: 0.82,
              metrics: [{ metricId: `${missionId}-metric-2`, name: "tests", value: 18, target: 20, achieved: false }],
            },
          },
        ],
      }],
    });

    await openAndWait(page, "/loops", "Loop Console");
    await expect(page.getByTestId("iteration-records")).toContainText("テスト 6 件失敗");
    await expect(page.getByTestId("iteration-records")).toContainText("0.82");
    await expect(page.locator(".wa-cond").filter({ hasText: "停止条件" })).toContainText("tests 18/20");

    await page.getByRole("button", { name: "現在の反復で打ち切り", exact: true }).click();
    await expect(page.getByText("人が打ち切り", { exact: true })).toBeVisible();
  });
});
