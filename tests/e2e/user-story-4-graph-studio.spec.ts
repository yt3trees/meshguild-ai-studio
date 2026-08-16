import { test, expect, openAndWait } from "./support/fixtures";

test.describe("User Story 4 - graph engineering", () => {
  test("renders graph nodes and validates the explicit loop graph", async ({ page }) => {
    await openAndWait(page, "/graphs", "Graph Studio");
    await page.getByTestId("graph-list-item").filter({ hasText: "demo-graph" }).click();

    await expect(page.getByTestId("graph-canvas")).toContainText("start");
    await expect(page.getByTestId("graph-canvas")).toContainText("verify");
    await expect(page.getByTestId("graph-node")).toHaveCount(9);

    const boxes = await page.getByTestId("graph-node").evaluateAll((elements) =>
      elements.map((element) => {
        const { left, top, right, bottom } = element.getBoundingClientRect();
        return { left, top, right, bottom };
      }),
    );
    for (let first = 0; first < boxes.length; first += 1) {
      for (let second = first + 1; second < boxes.length; second += 1) {
        const overlaps =
          boxes[first].left < boxes[second].right &&
          boxes[second].left < boxes[first].right &&
          boxes[first].top < boxes[second].bottom &&
          boxes[second].top < boxes[first].bottom;
        expect(overlaps, `graph nodes ${first} and ${second} must not overlap`).toBe(false);
      }
    }

    await page.getByTestId("graph-validate").click();
    await expect(page.getByTestId("diagnostics-valid")).toContainText("このグラフは検証を通りました。");
  });
});
