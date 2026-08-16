import type { TestInfo } from "@playwright/test";

export type ModelFixture = {
  name: string;
  projectEndpoint: string;
  deploymentName: string;
};

export type ApprovalFixture = {
  runId: string;
  tool: string;
  argsSummary: string;
  timeoutSeconds: number;
};

export function uniqueSuffix(testInfo: TestInfo): string {
  const testId = testInfo.testId.replace(/[^a-zA-Z0-9]+/g, "-").replace(/^-|-$/g, "");
  return `${testId.slice(-32)}-r${testInfo.retry}`;
}

export function modelFixture(testInfo: TestInfo): ModelFixture {
  const suffix = uniqueSuffix(testInfo);
  return {
    name: `E2E Model ${suffix}`,
    projectEndpoint: "https://example.test/projects/e2e",
    deploymentName: `e2e-model-${suffix}`,
  };
}

export function chatPrompt(testInfo: TestInfo): string {
  return `E2E deterministic prompt ${uniqueSuffix(testInfo)}`;
}

export function approvalFixture(testInfo: TestInfo, decision: "approve" | "reject"): ApprovalFixture {
  const suffix = uniqueSuffix(testInfo);
  return {
    runId: `e2e-run-${suffix}-${decision}`,
    tool: `e2e.test-tool.${decision}`,
    argsSummary: `safe ${decision} test arguments`,
    timeoutSeconds: 300,
  };
}
