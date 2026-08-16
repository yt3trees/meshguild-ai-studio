import fs from "node:fs/promises";

const RETRIES = 5;
const RETRY_DELAY_MS = 250;

export async function cleanupRunRoot(runRoot: string): Promise<void> {
  if (process.env.WORKAGENTS_E2E_KEEP_ROOT === "1") {
    return;
  }

  for (let attempt = 0; attempt < RETRIES; attempt += 1) {
    try {
      await fs.rm(runRoot, { recursive: true, force: true });
      return;
    } catch (error) {
      if (attempt === RETRIES - 1) {
        console.warn(`Could not remove E2E run root; retaining it for diagnostics: ${runRoot}`, error);
        return;
      }
      await new Promise((resolve) => setTimeout(resolve, RETRY_DELAY_MS));
    }
  }
}

export default async function globalTeardown(): Promise<void> {
  const runRoot = process.env.WORKAGENTS_E2E_RUN_ROOT;
  if (runRoot) {
    await cleanupRunRoot(runRoot);
  }
}
