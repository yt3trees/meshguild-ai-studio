import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";

const prefix = "work-agents-e2e-";
const entries = await fs.readdir(os.tmpdir(), { withFileTypes: true });
let retained = 0;

for (const entry of entries) {
  if (!entry.isDirectory() || !entry.name.startsWith(prefix)) {
    continue;
  }

  const candidate = path.join(os.tmpdir(), entry.name);
  try {
    await fs.rm(candidate, { recursive: true, force: true });
  } catch {
    retained += 1;
    console.warn(`Retained locked E2E run root: ${candidate}`);
  }
}

if (retained > 0) {
  process.exitCode = 1;
}
