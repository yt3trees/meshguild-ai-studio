import { test, expect, openAndWait } from "./support/fixtures";
import { chatPrompt, modelFixture } from "./support/test-data";

test.describe("Chat", () => {
  test("disables sending when the selected agent has no model", async ({ page, ready }) => {
    await openAndWait(page, "/chat", "Agents");
    await page.getByRole("button", { name: /Test Agent/i }).click();
    await ready("Test Agent");

    await expect(page.getByRole("alert")).toContainText("No model configured");
    await expect(page.getByTitle("送信")).toBeDisabled();
  });

  test("selects an agent and sends a prompt with a cleared input", async ({ page, ready }, testInfo) => {
    const model = modelFixture(testInfo);
    const prompt = chatPrompt(testInfo);

    await createModel(page, ready, model);
    await openAndWait(page, "/chat", "Agents");
    await page.getByRole("button", { name: /Test Agent/i }).click();
    await ready("Test Agent");

    await expect(page.locator("#agentSelect")).toHaveValue("test-agent");
    await expect(page.getByTitle("送信")).toBeDisabled();

    const input = page.getByRole("textbox", { name: "メッセージ" });
    await input.fill(prompt);
    await expect(page.getByTitle("送信")).toBeEnabled();
    await page.getByTitle("送信").click();

    await expect(page.getByTestId("user-message")).toContainText(prompt);
    await expect(input).toHaveValue("");
    await expect(page.getByTestId("agent-response")).toContainText(`E2E response: ${prompt}`);

    await deleteModel(page, ready, model.name);
  });
});

async function createModel(
  page: Parameters<typeof openAndWait>[0],
  ready: (heading?: string) => Promise<void>,
  model: ReturnType<typeof modelFixture>,
): Promise<void> {
  await openAndWait(page, "/models", "LLM models");
  await page.getByRole("button", { name: /Add model/ }).first().click();
  await ready("LLM models");
  await page.getByLabel("Name", { exact: true }).fill(model.name);
  await page.getByLabel("Project endpoint", { exact: true }).fill(model.projectEndpoint);
  await page.getByLabel("Deployment / model name", { exact: true }).fill(model.deploymentName);
  await page.getByRole("button", { name: "Save model", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("Model settings saved.");
}

async function deleteModel(
  page: Parameters<typeof openAndWait>[0],
  ready: (heading?: string) => Promise<void>,
  modelName: string,
): Promise<void> {
  await openAndWait(page, "/models", "LLM models");
  await ready("LLM models");
  const row = page.getByTestId("model-row").filter({ hasText: modelName });
  await row.getByTitle("モデルを削除").click();
  await expect(row).toHaveCount(0);
}
