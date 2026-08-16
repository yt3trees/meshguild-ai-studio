import { expect, type Page } from "@playwright/test";

export type NavigationTarget = {
  conceptualName: string;
  linkName?: string;
  entryPath: string;
  finalPath: string;
  heading: string;
  extraLocator?: string;
};

export const navigationTargets: NavigationTarget[] = [
  { conceptualName: "Mission Control", linkName: "Mission Control", entryPath: "/", finalPath: "/", heading: "Mission Control" },
  { conceptualName: "Team Room", linkName: "Team Room", entryPath: "/team-room", finalPath: "/team-room", heading: "Team Room" },
  { conceptualName: "Loop Console", linkName: "Loop Console", entryPath: "/loops", finalPath: "/loops", heading: "Loop Console" },
  { conceptualName: "Approvals", linkName: "Approvals", entryPath: "/approvals", finalPath: "/approvals", heading: "Approvals" },
  { conceptualName: "Graph Studio", linkName: "Graph Studio", entryPath: "/graphs", finalPath: "/graphs", heading: "Graph Studio" },
  { conceptualName: "Teams & Agents", linkName: "Teams & Agents", entryPath: "/teams-agents", finalPath: "/teams-agents", heading: "Teams & Agents" },
  { conceptualName: "Triggers", linkName: "Triggers", entryPath: "/triggers", finalPath: "/triggers", heading: "Triggers" },
  { conceptualName: "Replay & Audit", linkName: "Replay & Audit", entryPath: "/replay", finalPath: "/replay", heading: "Replay & Audit" },
];

export function mainNavigation(page: Page) {
  return page.getByRole("navigation", { name: "メインナビゲーション" });
}

export async function assertNavigationTarget(page: Page, target: NavigationTarget): Promise<void> {
  await expect(page).toHaveURL(new RegExp(`${escapeRegExp(target.finalPath)}/?$`));
  await expect(page.getByRole("heading", { name: target.heading, exact: true })).toBeVisible();
  if (target.extraLocator) {
    await expect(page.locator(target.extraLocator)).toBeVisible();
  }
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
