import os from "node:os";
import path from "node:path";
import { defineConfig } from "@playwright/test";

const isCI = Boolean(process.env.CI);
const repoRoot = __dirname;
const runRoot = process.env.WORKAGENTS_E2E_RUN_ROOT
  ?? path.join(os.tmpdir(), `work-agents-e2e-${Date.now()}-${process.pid}`);
const baseURL = "http://127.0.0.1:5049";
const screenshotMode = process.env.PW_SCREENSHOT === "on" ? "on" : "only-on-failure";

const inheritedEnvironment = Object.fromEntries(
  [
    "PATH",
    "Path",
    "SystemRoot",
    "WINDIR",
    "TEMP",
    "TMP",
    "USERPROFILE",
    "LOCALAPPDATA",
    "APPDATA",
    "PROGRAMFILES",
    "ProgramFiles",
    "ProgramFiles(x86)",
    "ComSpec",
    "PATHEXT",
    "DOTNET_ROOT",
    "DOTNET_ROOT_X64",
    "NUGET_PACKAGES",
  ]
    .map((key) => [key, process.env[key]] as const)
    .filter((entry): entry is readonly [string, string] => entry[1] !== undefined),
);

process.env.WORKAGENTS_E2E_RUN_ROOT = runRoot;

const runPath = (name: string) => path.join(runRoot, name);
process.env.WORKAGENTS_E2E_WORKSPACE_ROOT = runPath("workspace");

export default defineConfig({
  testDir: "./tests/e2e",
  outputDir: "test-results/artifacts",
  globalTimeout: 300_000,
  timeout: isCI ? 60_000 : 120_000,
  expect: {
    timeout: isCI ? 10_000 : 15_000,
  },
  fullyParallel: false,
  workers: 1,
  forbidOnly: isCI,
  retries: isCI ? 1 : 0,
  failOnFlakyTests: isCI,
  reporter: [
    [isCI ? "dot" : "list"],
    ["html", { outputFolder: "test-results/html-report", open: "never" }],
    ["junit", { outputFile: "test-results/junit/results.xml", stripANSIControlSequences: true }],
  ],
  globalTeardown: require.resolve("./tests/e2e/support/cleanup"),
  use: {
    baseURL,
    browserName: "chromium",
    headless: isCI,
    viewport: { width: 1440, height: 900 },
    locale: "en-US",
    timezoneId: "UTC",
    navigationTimeout: isCI ? 30_000 : 60_000,
    actionTimeout: isCI ? 10_000 : 20_000,
    screenshot: screenshotMode,
    trace: isCI ? "on-first-retry" : "retain-on-failure",
    video: "off",
    testIdAttribute: "data-pw",
  },
  webServer: {
    command: 'dotnet run --project "src/WorkAgents.Web/WorkAgents.Web.csproj" --no-launch-profile -- --urls http://127.0.0.1:5049',
    cwd: repoRoot,
    url: `${baseURL}/`,
    timeout: 180_000,
    reuseExistingServer: false,
    stdout: "pipe",
    stderr: "pipe",
    env: {
      ...inheritedEnvironment,
      ASPNETCORE_ENVIRONMENT: "E2E",
      DOTNET_ENVIRONMENT: "E2E",
      ASPNETCORE_URLS: baseURL,
      Profile: "Local",
      Runs__DatabasePath: runPath("state/work-agents.db"),
      SecretStore__Root: runPath("secrets"),
      Workspace__Root: process.env.WORKAGENTS_E2E_WORKSPACE_ROOT,
      Artifacts__Root: runPath("artifacts"),
      Orchestration__Engine__Enabled: "false",
      Orchestration__HostBaseUrl: baseURL,
      E2E__DeterministicAgentResponse: "true",
      OTEL_CONSOLE_DISABLED: "true",
    },
  },
});
