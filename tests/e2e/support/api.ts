import type { APIRequestContext } from "@playwright/test";
import type { ApprovalFixture } from "./test-data";

export type ApprovalStatusResponse = {
  approvalId: string;
  status: "Pending" | "Approved" | "Rejected" | "Expired";
  runId: string;
  tool: string;
};

export async function createApproval(
  request: APIRequestContext,
  fixture: ApprovalFixture,
): Promise<{ approvalId: string; status: "Pending" }> {
  const response = await request.post("/__e2e/approvals", { data: fixture });
  if (!response.ok()) {
    throw new Error(`Approval seed failed with HTTP ${response.status()}.`);
  }
  return (await response.json()) as { approvalId: string; status: "Pending" };
}

export async function getApproval(
  request: APIRequestContext,
  approvalId: string,
): Promise<ApprovalStatusResponse> {
  const response = await request.get(`/__e2e/approvals/${encodeURIComponent(approvalId)}`);
  if (!response.ok()) {
    throw new Error(`Approval status lookup failed with HTTP ${response.status()}.`);
  }
  return (await response.json()) as ApprovalStatusResponse;
}

export type OrchestrationSeed = {
  mission?: {
    missionId: string;
    goal: string;
    targetKind?: string;
    targetName?: string;
    teamName?: string;
    status?: string;
    outcome?: string;
    stopReason?: string;
    triggerKind?: string;
    budget?: {
      costLimitUsd?: number;
      timeLimitSeconds?: number;
      maxIterations?: number;
      maxConcurrentAgents?: number;
      costUsedUsd?: number;
      elapsedSeconds?: number;
      iterationsUsed?: number;
      peakConcurrentAgents?: number;
    };
  };
  agents?: Array<{
    instanceId: string;
    agentName: string;
    role?: string;
    instanceNo?: number;
    state?: string;
    missionId?: string;
    awaitingInstanceId?: string;
    modelName?: string;
  }>;
  messages?: Array<{
    body: string;
    kind?: string;
    senderKind?: string;
    missionId?: string;
    messageId?: string;
    senderInstanceId?: string;
    recipientInstanceId?: string;
    delegationDepth?: number;
    inputRefs?: string;
    costRecordId?: string;
    secondsAgo?: number;
  }>;
  loops?: Array<{
    loopRunId: string;
    nodeRunId?: string;
    missionId?: string;
    maxIterations?: number;
    costLimitUsd?: number;
    timeLimitSeconds?: number;
    scoreThreshold?: number;
    iterations?: Array<{
      iterationId: string;
      iterationNo: number;
      state?: string;
      inputJson?: string;
      outputJson?: string;
      costUsd?: number;
      tokens?: number;
      durationMs?: number;
      evaluation?: {
        evaluationId: string;
        score: number;
        evaluatorKind?: string;
        evaluatorRef?: string;
        notes?: string;
        passed?: boolean;
        metrics?: Array<{ metricId: string; name: string; value: number; target: number; achieved: boolean }>;
      };
    }>;
  }>;
  triggers?: Array<{
    triggerId: string;
    name: string;
    kind?: string;
    targetKind?: string;
    targetName?: string;
    input?: string;
    cron?: string;
    intervalSeconds?: number;
    overlapPolicy?: string;
    enabled?: boolean;
    secretRef?: string;
  }>;
  approvals?: Array<{
    approvalId: string;
    runId: string;
    tool: string;
    argsSummary: string;
    timeoutSeconds?: number;
    missionId?: string;
    agentInstanceId?: string;
    nodeRunId?: string;
    iterationId?: string;
  }>;
  artifacts?: Array<{
    artifactId: string;
    path: string;
    summary: string;
    contentHash: string;
    sourceMessageId?: string;
    missionId?: string;
    iterationId?: string;
  }>;
};

export async function seedOrchestration(
  request: APIRequestContext,
  seed: OrchestrationSeed,
): Promise<{ missionId?: string }> {
  const response = await request.post("/__e2e/orchestration", { data: seed });
  if (!response.ok()) {
    throw new Error(`Orchestration seed failed with HTTP ${response.status()}: ${await response.text()}`);
  }
  return (await response.json()) as { missionId?: string };
}
