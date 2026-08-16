import { test, expect, openAndWait } from "./support/fixtures";
import { seedOrchestration } from "./support/api";

test.describe("User Story 1 - team conversation", () => {
  test("shows delegation and a direct agent question/answer in order", async ({ page, request, testSuffix }) => {
    const missionId = `e2e-us1-${testSuffix}`;
    const orchestratorId = `${missionId}-orchestrator`;
    const specId = `${missionId}-spec`;
    const devId = `${missionId}-dev`;

    await seedOrchestration(request, {
      mission: {
        missionId,
        goal: "E2E: implement feature X and pass the tests",
        targetName: "demo-team",
        teamName: "demo-team",
        status: "Running",
        budget: { costUsedUsd: 1.24, iterationsUsed: 1 },
      },
      agents: [
        { instanceId: orchestratorId, agentName: "orchestrator-agent", role: "Orchestrator", state: "Thinking" },
        { instanceId: specId, agentName: "spec-research-agent", state: "Completed" },
        { instanceId: devId, agentName: "dev-agent", state: "AwaitingReply", awaitingInstanceId: specId },
      ],
      messages: [
        {
          messageId: `${missionId}-delegate`,
          senderInstanceId: orchestratorId,
          recipientInstanceId: devId,
          kind: "Delegate",
          body: "実装対象を確認し、仕様調査の結果を参照して進めてください。",
          delegationDepth: 1,
          secondsAgo: 30,
        },
        {
          messageId: `${missionId}-question`,
          senderInstanceId: devId,
          recipientInstanceId: specId,
          kind: "Question",
          body: "空入力時の期待値を確認してください。",
          delegationDepth: 2,
          secondsAgo: 20,
        },
        {
          messageId: `${missionId}-answer`,
          senderInstanceId: specId,
          recipientInstanceId: devId,
          kind: "Answer",
          body: "未指定の項目は省略するのが正です。",
          delegationDepth: 2,
          inputRefs: "spec.md#3.2",
          secondsAgo: 10,
        },
      ],
    });

    await openAndWait(page, `/missions/${missionId}`, "Team Room");
    const thread = page.getByTestId("message-thread");
    await expect(thread).toContainText("実装対象を確認");
    await expect(thread).toContainText("空入力時の期待値");
    await expect(thread).toContainText("未指定の項目は省略");
    await expect(page.getByTestId("message")).toHaveCount(3);
    await expect(page.getByTestId("team-room")).toContainText("orchestrator-agent");
    await expect(page.getByTestId("team-room")).toContainText("完了待ち");

    await seedOrchestration(request, {
      messages: [{ missionId, kind: "Report", senderInstanceId: devId, body: "新しい進捗: 実装差分を作成しました。" }],
    });
    await expect(thread).toContainText("新しい進捗", { timeout: 8_000 });
  });
});
