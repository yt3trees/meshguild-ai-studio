import { test, expect, openAndWait } from "./support/fixtures";
import { seedOrchestration } from "./support/api";

test.describe("User Story 2 - human control", () => {
  test("posts an intervention, pauses/resumes a mission, and exposes approval control", async ({ page, request, testSuffix }) => {
    const missionId = `e2e-us2-${testSuffix}`;
    const devId = `${missionId}-dev`;

    await seedOrchestration(request, {
      mission: { missionId, goal: "E2E: human control points", status: "Running" },
      agents: [{ instanceId: devId, agentName: "dev-agent", state: "AwaitingApproval" }],
      approvals: [{
        approvalId: `${missionId}-approval`,
        runId: `${missionId}-run`,
        tool: "shell",
        argsSummary: "dotnet test WorkAgents.sln",
        missionId,
        agentInstanceId: devId,
      }],
    });

    await openAndWait(page, `/missions/${missionId}`, "Team Room");
    await expect(page.getByTestId("team-room")).toContainText("承認待ち");
    await expect(page.getByTestId("team-room")).toContainText("dotnet test WorkAgents.sln");

    const instruction = page.getByPlaceholder("方針の訂正や追加情報を入力すると、次の発言から反映されます");
    await instruction.fill("人の指示: 四捨五入を正としてください。");
    await page.getByRole("button", { name: "送信", exact: true }).click();
    await expect(page.getByTestId("message-thread")).toContainText("四捨五入を正");

    await page.getByRole("button", { name: "一時停止", exact: true }).click();
    await expect(page.getByTestId("team-room")).toContainText("Paused");
    await page.getByRole("button", { name: "再開", exact: true }).click();
    await expect(page.getByTestId("team-room")).toContainText("Running");
  });
});
