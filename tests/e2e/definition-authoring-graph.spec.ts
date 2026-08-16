import { test, expect, openAndWait } from "./support/fixtures";

/**
 * グラフ定義を GUI で編集できること (案B) と、
 * 検証結果が日本語で返ること (案D)、動きの説明が出ること (案C) を確かめる。
 * 保存は行わないため、このスペックは既存の定義を変更しない。
 */
test.describe("Graph Studio - schema driven editing", () => {
  test("shows only the fields that the selected node kind uses", async ({ page }) => {
    await openAndWait(page, "/graphs/demo-graph");

    // kind: loop のノードでは、ループ本体と停止条件が出てエージェント欄は出ない。
    await page.getByTestId("graph-node").filter({ hasText: "verify" }).click();
    await expect(page.getByTestId("schema-field-node.id")).toHaveValue("verify");
    await expect(page.getByTestId("schema-field-node.kind")).toHaveValue("loop");
    await expect(page.getByTestId("schema-field-node.body")).toBeVisible();
    await expect(page.getByTestId("schema-field-node.stop.maxIterations")).toHaveValue("2");
    await expect(page.getByTestId("schema-field-node.agent")).toHaveCount(0);
    await expect(page.getByTestId("schema-field-node.codeFile")).toHaveCount(0);

    // kind: agent のノードでは逆に、エージェントと入力だけが出る。
    await page.getByTestId("graph-node").filter({ hasText: "start" }).click();
    await expect(page.getByTestId("schema-field-node.agent")).toBeVisible();
    await expect(page.getByTestId("schema-field-node.input")).toBeVisible();
    await expect(page.getByTestId("schema-field-node.body")).toHaveCount(0);
    await expect(page.getByTestId("schema-field-node.stop.maxIterations")).toHaveCount(0);
  });

  test("offers existing agents as a dropdown instead of free text", async ({ page }) => {
    await openAndWait(page, "/graphs/demo-graph");
    await page.getByTestId("graph-node").filter({ hasText: "start" }).click();

    const agentField = page.getByTestId("schema-field-node.agent");
    await expect(agentField).toHaveJSProperty("tagName", "SELECT");
    await expect(agentField).toHaveValue("repo-agent");
    // YAML に書かれるのは name なので、表示名ではなく name が選択肢のラベルになっている。
    await expect(agentField.locator('option[value="dev-agent"]')).toContainText("dev-agent");
    await expect(agentField.locator('option[value="meeting-agent"]')).toHaveCount(1);
  });

  test("switching the node kind hides the fields that no longer apply", async ({ page }) => {
    await openAndWait(page, "/graphs/demo-graph");
    await page.getByTestId("graph-node").filter({ hasText: "start" }).click();

    await page.getByTestId("schema-field-node.kind").selectOption("approval");

    await expect(page.getByTestId("schema-field-node.title")).toBeVisible();
    await expect(page.getByTestId("schema-field-node.timeoutSeconds")).toBeVisible();
    await expect(page.getByTestId("schema-field-node.agent")).toHaveCount(0);
    await expect(page.getByTestId("graph-dirty")).toBeVisible();
  });

  test("explains a missing codeFile in Japanese with a fix", async ({ page }) => {
    await openAndWait(page, "/graphs/demo-graph");

    await page.getByTestId("graph-add-node").click();
    await page.getByTestId("schema-field-node.kind").selectOption("code");
    await page.getByTestId("graph-validate").click();

    const diagnostics = page.getByTestId("diagnostics");
    await expect(diagnostics).toContainText("codeFile が必要です");
    await expect(diagnostics).toContainText("グラフフォルダーからの相対パス");
  });

  test("narrates what the graph does and previews the YAML that would be written", async ({ page }) => {
    await openAndWait(page, "/graphs/demo-graph");

    await expect(page.getByTestId("narration")).toContainText("start から始まる");
    await expect(page.getByTestId("narration")).toContainText("エージェント repo-agent");

    await page.getByTestId("graph-show-yaml").click();
    const yaml = page.getByTestId("graph-yaml");
    await expect(yaml).toContainText("version: 1");
    await expect(yaml).toContainText('name: "demo-graph"');
    await expect(yaml).toContainText("loopBack: true");
  });

  test("keeps the save button disabled until something changes", async ({ page }) => {
    await openAndWait(page, "/graphs/demo-graph");

    await expect(page.getByTestId("graph-save")).toBeDisabled();

    await page.getByTestId("inspector-tab-edges").click();
    await page.getByTestId("graph-add-edge").click();

    await expect(page.getByTestId("graph-save")).toBeEnabled();
    await expect(page.getByTestId("graph-dirty")).toBeVisible();
  });
});
