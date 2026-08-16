import { test, expect, openAndWait } from "./support/fixtures";

/**
 * チーム定義をフォームで編集できること、参照がすべて選択式になっていること (案A)、
 * 上限の矛盾が日本語で返ること (案D) を確かめる。保存は行わない。
 */
test.describe("Team editor - schema driven editing", () => {
  test("explains the team on the detail page before editing", async ({ page }) => {
    await openAndWait(page, "/teams-agents/demo-team");

    const narration = page.getByTestId("narration");
    await expect(narration).toContainText("統括は orchestrator-agent");
    await expect(narration).toContainText("dev-agent");
    await expect(narration).toContainText("委譲は 3 段まで");
    await expect(page.getByTestId("team-edit-link")).toBeVisible();
  });

  test("opens the editor from the detail page with the roster loaded", async ({ page }) => {
    await openAndWait(page, "/teams-agents/demo-team");
    await page.getByTestId("team-edit-link").click();

    await expect(page).toHaveURL(/\/teams-agents\/demo-team\/edit$/);
    await expect(page.getByTestId("schema-field-name")).toHaveValue("demo-team");
    await expect(page.getByTestId("schema-field-orchestrator.agent")).toHaveValue("orchestrator-agent");
    await expect(page.getByTestId("member-card")).toHaveCount(3);
    await expect(page.getByTestId("channel-card")).toHaveCount(2);
  });

  test("picks team members from the registered agents", async ({ page }) => {
    await openAndWait(page, "/teams-agents/demo-team/edit");

    const orchestrator = page.getByTestId("schema-field-orchestrator.agent");
    await expect(orchestrator).toHaveJSProperty("tagName", "SELECT");
    await expect(orchestrator.locator('option[value="meeting-agent"]')).toHaveCount(1);

    // 直接会話の相手は、チーム内のエージェントだけに絞られる。
    const channelFrom = page.getByTestId("schema-field-channels.allow[].from").first();
    await expect(channelFrom.locator('option[value="orchestrator-agent"]')).toHaveCount(1);
    await expect(channelFrom.locator('option[value="meeting-agent"]')).toHaveCount(0);
  });

  test("reports a parallel limit conflict in Japanese", async ({ page }) => {
    await openAndWait(page, "/teams-agents/demo-team/edit");

    await page.getByTestId("schema-field-limits.maxParallelInstances").fill("1");
    await page.getByTestId("schema-field-limits.maxParallelInstances").blur();
    await page.getByTestId("team-validate").click();

    const diagnostics = page.getByTestId("diagnostics");
    await expect(diagnostics).toContainText("limits.maxParallelInstances を超えています");
    await expect(diagnostics).toContainText("合計以上に上げてください");
  });

  test("validates cleanly and previews the YAML that would be written", async ({ page }) => {
    await openAndWait(page, "/teams-agents/demo-team/edit");

    await page.getByTestId("team-validate").click();
    await expect(page.getByTestId("diagnostics-valid")).toContainText("このチームは検証を通りました。");

    const yaml = page.getByTestId("team-yaml");
    await expect(yaml).toContainText('name: "demo-team"');
    await expect(yaml).toContainText('agent: "orchestrator-agent"');
    await expect(yaml).toContainText("kinds:");
  });

  test("removing a member also drops the channels that referenced them", async ({ page }) => {
    await openAndWait(page, "/teams-agents/demo-team/edit");

    // dev-agent は 2 本の直接会話経路に登場する。外すと経路も一緒に消える。
    await page
      .getByTestId("member-card")
      .filter({ has: page.locator('select[data-pw="schema-field-members[].agent"]') })
      .nth(1)
      .getByRole("button", { name: "削除" })
      .click();

    await expect(page.getByTestId("member-card")).toHaveCount(2);
    await expect(page.getByTestId("channel-card")).toHaveCount(0);
    await expect(page.getByTestId("team-dirty")).toBeVisible();
  });
});
