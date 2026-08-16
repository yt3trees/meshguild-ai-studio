import { test, expect, openAndWait } from "./support/fixtures";
import { seedOrchestration } from "./support/api";
import { workspaceRoot, writeMissionWorkspaceFile } from "./support/workspace";

test.describe("User Story 4 - shared workspace files", () => {
  test("shows read-only workspace metadata and refreshes file changes", async ({ page, request, testSuffix }) => {
    const missionId = `e2e-us4-${testSuffix}`;
    await seedOrchestration(request, {
      mission: {
        missionId,
        goal: "E2E: observe shared workspace files",
        status: "Running",
      },
    });

    await openAndWait(page, `/missions/${missionId}`, "Team Room");
    const room = page.getByTestId("team-room");
    await expect(room).toContainText("共有ファイル");
    await expect(room).toContainText("共有作業フォルダは空です。");

    await writeMissionWorkspaceFile(missionId, "reports/spec.md", "initial content");
    await expect(room).toContainText("reports/spec.md", { timeout: 8_000 });
    await expect(room).toContainText("ファイル");
    await expect(room).toContainText("15 B");

    await page.getByLabel("共有ファイルを検索").fill("reports");
    await expect(room).toContainText("reports/spec.md");
    await page.getByLabel("共有ファイルを検索").fill("");

    await writeMissionWorkspaceFile(missionId, "reports/spec.md", "updated content with a longer body");
    await expect(room.getByText("更新", { exact: true }).first()).toBeVisible({ timeout: 8_000 });

    await expect(room).not.toContainText(workspaceRoot());
    await expect(room).not.toContainText("updated content with a longer body");
    await expect(room.getByRole("button", { name: /編集|削除|ダウンロード/ })).toHaveCount(0);
  });
});
