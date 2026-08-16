import fs from "node:fs/promises";
import path from "node:path";

function e2eRunRoot(): string {
  const root = process.env.WORKAGENTS_E2E_RUN_ROOT;
  if (!root) {
    throw new Error("WORKAGENTS_E2E_RUN_ROOT is not configured.");
  }
  return root;
}

export function workspaceRoot(): string {
  return process.env.WORKAGENTS_E2E_WORKSPACE_ROOT ?? path.join(e2eRunRoot(), "workspace");
}

export function missionWorkspacePath(missionId: string): string {
  if (!/^[A-Za-z0-9_-]+$/.test(missionId)) {
    throw new Error(`Invalid mission ID: ${missionId}`);
  }
  return path.join(workspaceRoot(), "missions", missionId, "work");
}

export async function writeMissionWorkspaceFile(
  missionId: string,
  relativePath: string,
  content: string,
): Promise<string> {
  const root = missionWorkspacePath(missionId);
  const target = path.resolve(root, relativePath);
  const rootWithSeparator = root.endsWith(path.sep) ? root : `${root}${path.sep}`;
  if (!target.startsWith(rootWithSeparator)) {
    throw new Error("Workspace fixture path escaped the mission workspace.");
  }
  await fs.mkdir(path.dirname(target), { recursive: true });
  await fs.writeFile(target, content, "utf8");
  return target;
}

export async function removeMissionWorkspace(missionId: string): Promise<void> {
  await fs.rm(missionWorkspacePath(missionId), { recursive: true, force: true });
}
