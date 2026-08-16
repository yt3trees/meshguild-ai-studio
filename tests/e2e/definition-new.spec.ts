import { test, expect, openAndWait } from "./support/fixtures";

/**
 * 白紙から書かせない導線 (案E) の確認。
 * 作成した定義は実行ホストの定義フォルダーに残るため、名前は毎回一意にする。
 */
const unique = (prefix: string) => `${prefix}-${Date.now().toString(36)}`;

test.describe("New definition - templates and duplication", () => {
  test("lists team templates with what they are for", async ({ page }) => {
    await openAndWait(page, "/definitions/new?kind=team");

    const cards = page.getByTestId("template-card");
    await expect(cards).toHaveCount(3);
    await expect(cards.first()).toContainText("レビュー体制");
    await expect(cards.first()).toContainText("別の目で確かめてから出したいとき");
  });

  test("fills template slots from existing agents and previews the behaviour", async ({ page }) => {
    await openAndWait(page, "/definitions/new?kind=team");
    await page.getByTestId("template-card").filter({ hasText: "レビュー体制" }).click();

    // スロットは既存エージェントから重複しないように仮置きされる。
    await expect(page.getByTestId("template-slot-lead")).not.toHaveValue("");
    await expect(page.getByTestId("template-slot-worker")).not.toHaveValue("");
    await expect(page.getByTestId("template-slot-checker")).not.toHaveValue("");

    await page.getByTestId("new-name").fill(unique("preview-team"));
    await page.getByTestId("new-name").blur();

    await expect(page.getByTestId("narration")).toContainText("統括は");
    await expect(page.getByTestId("narration")).toContainText("会話はすべて統括を経由する");
  });

  test("rejects a name that is already taken", async ({ page }) => {
    await openAndWait(page, "/definitions/new?kind=team");
    await page.getByTestId("template-card").filter({ hasText: "レビュー体制" }).click();

    await page.getByTestId("new-name").fill("demo-team");
    await page.getByTestId("new-name").blur();

    await expect(page.getByTestId("new-name-error")).toContainText("既にあります");
    await expect(page.getByTestId("new-create")).toBeDisabled();
  });

  test("rejects a name that cannot be a folder name", async ({ page }) => {
    await openAndWait(page, "/definitions/new?kind=team");
    await page.getByTestId("template-card").filter({ hasText: "レビュー体制" }).click();

    await page.getByTestId("new-name").fill("Team Name!");
    await page.getByTestId("new-name").blur();

    await expect(page.getByTestId("new-name-error")).toContainText("英小文字、数字、ハイフン");
  });

  test("creates a team from a template and lands in the editor", async ({ page }) => {
    const name = unique("e2e-team");
    await openAndWait(page, "/definitions/new?kind=team");
    await page.getByTestId("template-card").filter({ hasText: "レビュー体制" }).click();
    await page.getByTestId("new-name").fill(name);
    await page.getByTestId("new-name").blur();

    await expect(page.getByTestId("new-create")).toBeEnabled();
    await page.getByTestId("new-create").click();

    await expect(page).toHaveURL(new RegExp(`/teams-agents/${name}/edit$`));
    await expect(page.getByTestId("schema-field-name")).toHaveValue(name);
    await expect(page.getByTestId("member-card")).toHaveCount(2);
    await expect(page.getByTestId("team-yaml")).toContainText(`name: "${name}"`);

    // 一覧にも即座に出る (保存後にディスクから読み直しているため)。
    await openAndWait(page, "/teams-agents", "Teams & Agents");
    await expect(page.getByTestId("team-list-item").filter({ hasText: name })).toHaveCount(1);
  });

  test("creates a graph from a template and opens it in Graph Studio", async ({ page }) => {
    const name = unique("e2e-graph");
    await openAndWait(page, "/definitions/new?kind=graph");
    await page.getByTestId("template-card").filter({ hasText: "直列 3 ステップ" }).click();
    await page.getByTestId("new-name").fill(name);
    await page.getByTestId("new-name").blur();

    await page.getByTestId("new-create").click();

    await expect(page).toHaveURL(new RegExp(`/graphs/${name}$`));
    await expect(page.getByTestId("graph-node")).toHaveCount(3);
    await expect(page.getByTestId("narration")).toContainText("plan から始まる");

    await page.getByTestId("graph-validate").click();
    await expect(page.getByTestId("diagnostics-valid")).toBeVisible();
  });

  test("duplicates an existing graph under a new name", async ({ page }) => {
    const name = unique("e2e-copy");
    await openAndWait(page, "/definitions/new?kind=graph");
    await page.getByTestId("new-source-copy").click();
    await page.getByTestId("copy-source").selectOption("demo-graph");
    await page.getByTestId("new-name").fill(name);
    await page.getByTestId("new-name").blur();

    await expect(page.getByTestId("narration")).toContainText("start から始まる");
    await page.getByTestId("new-create").click();

    await expect(page).toHaveURL(new RegExp(`/graphs/${name}$`));
    await expect(page.getByTestId("graph-node")).toHaveCount(9);
  });
});
