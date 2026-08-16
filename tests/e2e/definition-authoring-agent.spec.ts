import { test, expect, openAndWait } from "./support/fixtures";

/**
 * エージェント定義を画面から作り、共有スキルを割り当てて agent.yaml へ書き戻せること。
 * 作成した定義は実行ホストの定義フォルダーに残るため、名前は毎回一意にする。
 */
const unique = (prefix: string) => `${prefix}-${Date.now().toString(36)}`;

test.describe("Agent editor - skills and permissions", () => {
  test("lists the registered agents with their skills", async ({ page }) => {
    await openAndWait(page, "/teams-agents", "Teams & Agents");

    const row = page.getByTestId("agent-list-item").filter({ hasText: "meeting-agent" });
    await expect(row).toHaveCount(1);
    await expect(row).toContainText("meeting-minutes");
    await expect(row.getByTestId("agent-edit-link")).toBeVisible();
  });

  test("offers every skill on disk as a checkbox, not free text", async ({ page }) => {
    await openAndWait(page, "/agents/meeting-agent/edit");

    const skills = page.getByTestId("schema-field-skills");
    await expect(skills.getByRole("checkbox")).not.toHaveCount(0);
    await expect(skills.getByRole("checkbox", { name: "meeting-minutes" })).toBeChecked();
  });

  test("name is fixed in the editor because it is the folder name", async ({ page }) => {
    await openAndWait(page, "/agents/meeting-agent/edit");

    await expect(page.getByTestId("schema-field-name")).toBeDisabled();
  });

  test("creates an agent, attaches a skill and writes it to agent.yaml", async ({ page }) => {
    const name = unique("e2e-agent");
    await openAndWait(page, "/definitions/new?kind=agent");

    await page.getByTestId("new-name").fill(name);
    await page.getByTestId("new-name").blur();
    await expect(page.getByTestId("new-create")).toBeEnabled();
    await page.getByTestId("new-create").click();

    await expect(page).toHaveURL(new RegExp(`/agents/${name}/edit$`));
    await expect(page.getByTestId("agent-yaml")).toContainText(`name: "${name}"`);

    await page
      .getByTestId("schema-field-skills")
      .getByRole("checkbox", { name: "meeting-minutes" })
      .check();
    await expect(page.getByTestId("agent-yaml")).toContainText('- "meeting-minutes"');

    await page.getByTestId("agent-validate").click();
    await expect(page.getByTestId("diagnostics-valid")).toBeVisible();

    await page.getByTestId("agent-save").click();
    await expect(page.getByTestId("agent-saved")).toContainText("agent.yaml");

    // 一覧にも即座に出る (保存後にディスクから読み直しているため)。
    await openAndWait(page, "/teams-agents", "Teams & Agents");
    const row = page.getByTestId("agent-list-item").filter({ hasText: name });
    await expect(row).toHaveCount(1);
    await expect(row).toContainText("meeting-minutes");
  });
});
