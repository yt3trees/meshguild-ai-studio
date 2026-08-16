import { test, expect, openAndWait } from "./support/fixtures";
import { modelFixture } from "./support/test-data";

test.describe("Models", () => {
  test("shows native validation and does not save empty required fields", async ({ page, ready }, testInfo) => {
    const model = modelFixture(testInfo);
    await openAndWait(page, "/models", "LLM models");
    await page.getByRole("button", { name: /Add model/ }).first().click();
    await ready("LLM models");

    await expect(page.getByLabel("API key")).toBeVisible();
    await expect(page.getByRole("option", { name: "Azure OpenAI", exact: true })).toHaveCount(0);

    const name = page.getByLabel("Name", { exact: true });
    const endpoint = page.getByLabel("Project endpoint", { exact: true });
    const deployment = page.getByLabel("Deployment / model name", { exact: true });
    await page.getByRole("button", { name: "Save model", exact: true }).click();

    await expect.poll(async () => name.evaluate((element) => !(element as HTMLInputElement).validity.valid)).toBe(true);
    await expect.poll(async () => endpoint.evaluate((element) => !(element as HTMLInputElement).validity.valid)).toBe(true);
    await expect.poll(async () => deployment.evaluate((element) => !(element as HTMLInputElement).validity.valid)).toBe(true);
    await expect.poll(async () => name.evaluate((element) => (element as HTMLInputElement).validationMessage)).not.toBe("");
    await expect(page.getByTestId("model-row").filter({ hasText: model.name })).toHaveCount(0);
  });

  test("shows OpenAI model configuration", async ({ page, ready }) => {
    await openAndWait(page, "/models", "LLM models");
    await page.getByRole("button", { name: /Add model/ }).first().click();
    await ready("LLM models");

    await page.getByRole("combobox", { name: "Provider", exact: true }).selectOption("OpenAI");

    await expect(page.getByLabel("Project endpoint", { exact: true })).toHaveCount(0);
    await expect(page.getByText("api.openai.com/v1", { exact: false })).toBeVisible();
    await expect(page.getByRole("combobox", { name: "API", exact: true })).toBeVisible();
    await expect.poll(async () => page.getByRole("textbox", { name: /API key/ }).evaluate(
      (element) => (element as HTMLInputElement).required,
    )).toBe(true);
  });

  test("shows Amazon Bedrock model configuration", async ({ page, ready }) => {
    await openAndWait(page, "/models", "LLM models");
    await page.getByRole("button", { name: /Add model/ }).first().click();
    await ready("LLM models");

    await page.getByRole("combobox", { name: "Provider", exact: true }).selectOption("AmazonBedrock");

    await expect(page.getByRole("textbox", { name: "AWS region", exact: true })).toBeVisible();
    await expect(page.getByText("AWS SDKの標準認証チェーン", { exact: false })).toBeVisible();
    await expect(page.getByRole("textbox", { name: /API key/ })).toHaveCount(0);
  });

  test("shows OpenRouter model configuration", async ({ page, ready }) => {
    await openAndWait(page, "/models", "LLM models");
    await page.getByRole("button", { name: /Add model/ }).first().click();
    await ready("LLM models");

    await page.getByRole("combobox", { name: "Provider", exact: true }).selectOption("OpenRouter");

    await expect(page.getByLabel("Project endpoint", { exact: true })).toHaveCount(0);
    await expect(page.getByText("openrouter.ai/api/v1", { exact: false })).toBeVisible();
    await expect(page.getByRole("combobox", { name: "API", exact: true })).toHaveCount(0);
    await expect.poll(async () => page.getByRole("textbox", { name: /API key/ }).evaluate(
      (element) => (element as HTMLInputElement).required,
    )).toBe(true);
  });

  test("rejects an invalid project endpoint", async ({ page, ready }, testInfo) => {
    const model = modelFixture(testInfo);
    await openAndWait(page, "/models", "LLM models");
    await page.getByRole("button", { name: /Add model/ }).first().click();
    await ready("LLM models");

    await page.getByLabel("Name", { exact: true }).fill(model.name);
    const endpoint = page.getByLabel("Project endpoint", { exact: true });
    await endpoint.fill("not-a-url");
    await page.getByLabel("Deployment / model name", { exact: true }).fill(model.deploymentName);
    await page.getByRole("button", { name: "Save model", exact: true }).click();

    await expect.poll(async () => endpoint.evaluate((element) => !(element as HTMLInputElement).validity.valid)).toBe(true);
    await expect.poll(async () => endpoint.evaluate((element) => (element as HTMLInputElement).validationMessage)).not.toBe("");
    await expect(page.getByTestId("model-row").filter({ hasText: model.name })).toHaveCount(0);
  });

  test("saves a valid Foundry model and renders it in the list", async ({ page, ready }, testInfo) => {
    const model = modelFixture(testInfo);
    await openAndWait(page, "/models", "LLM models");
    await page.getByRole("button", { name: /Add model/ }).first().click();
    await ready("LLM models");

    await page.getByLabel("Name", { exact: true }).fill(model.name);
    await page.getByLabel("Project endpoint", { exact: true }).fill(model.projectEndpoint);
    await page.getByLabel("Deployment / model name", { exact: true }).fill(model.deploymentName);
    await page.getByRole("button", { name: "Save model", exact: true }).click();

    await expect(page.getByRole("status")).toContainText("Model settings saved.");
    const row = page.getByTestId("model-row").filter({ hasText: model.name });
    await expect(row).toContainText(model.projectEndpoint);
    await expect(row).toContainText(model.deploymentName);

    await row.getByTitle("モデルを削除").click();
    await expect(row).toHaveCount(0);
  });
});
