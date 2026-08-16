import json
import os
import time
from pathlib import Path

from playwright.sync_api import expect, sync_playwright


BASE_URL = os.environ.get("GIF_BASE_URL", "http://127.0.0.1:5049")
RUN_ROOT = Path(r"C:\Users\yatfo\AppData\Local\Temp\opencode\cursor-gifs-run")
TEAM_FRAME_DIR = RUN_ROOT / "team-frames"
APPROVAL_FRAME_DIR = RUN_ROOT / "approval-frames"
GRAPH_FRAME_DIR = RUN_ROOT / "graph-frames"
FRAME_INTERVAL = 1 / 12


def seed(request, payload):
    response = request.post(
        "/__e2e/orchestration",
        data=json.dumps(payload),
        headers={"Content-Type": "application/json"},
    )
    if not response.ok:
        raise RuntimeError(f"Seed request failed: HTTP {response.status} {response.text()}")


def create_approval(request, run_id, tool, args_summary):
    response = request.post(
        "/__e2e/approvals",
        data=json.dumps({
            "runId": run_id,
            "tool": tool,
            "argsSummary": args_summary,
            "timeoutSeconds": 300,
        }),
        headers={"Content-Type": "application/json"},
    )
    if not response.ok:
        raise RuntimeError(f"Approval seed failed: HTTP {response.status} {response.text()}")


def install_progress(page):
    page.evaluate(
        """
        () => {
            if (document.getElementById('gif-progress')) {
                return;
            }
            const style = document.createElement('style');
            style.id = 'gif-progress-style';
            style.textContent = `
                #gif-progress {
                    position: fixed;
                    z-index: 2147483645;
                    left: 24px;
                    right: 24px;
                    bottom: 12px;
                    display: flex;
                    align-items: center;
                    gap: 12px;
                    height: 34px;
                    padding: 0 12px;
                    color: #f5f7ff;
                    background: rgba(8, 12, 22, .94);
                    border: 1px solid #31446f;
                    border-radius: 6px;
                    box-shadow: 0 4px 16px rgba(0, 0, 0, .45);
                    pointer-events: none;
                    font: 600 12px/1 system-ui, sans-serif;
                }
                #gif-progress-label {
                    flex: 0 0 190px;
                    overflow: hidden;
                    text-overflow: ellipsis;
                    white-space: nowrap;
                }
                #gif-progress-track {
                    flex: 1;
                    height: 5px;
                    overflow: hidden;
                    background: #202b45;
                    border-radius: 999px;
                }
                #gif-progress-fill {
                    display: block;
                    width: 0;
                    height: 100%;
                    background: #76a5ff;
                    border-radius: inherit;
                }
            `;
            document.head.appendChild(style);
            const progress = document.createElement('div');
            progress.id = 'gif-progress';
            progress.innerHTML = `
                <span id="gif-progress-label"></span>
                <span id="gif-progress-track"><span id="gif-progress-fill"></span></span>
            `;
            document.body.appendChild(progress);
            window.__gifProgress = {
                set(step, total, label) {
                    document.getElementById('gif-progress-label').textContent = `${step} / ${total}  ${label}`;
                    document.getElementById('gif-progress-fill').style.width = `${Math.max(0, Math.min(100, step / total * 100))}%`;
                }
            };
        }
        """
    )


def set_progress(page, step, total, label):
    page.evaluate(
        "({ step, total, label }) => window.__gifProgress?.set(step, total, label)",
        {"step": step, "total": total, "label": label},
    )


def install_cursor(page):
    page.evaluate(
        """
        () => {
            document.getElementById('gif-cursor')?.remove();
            document.getElementById('gif-cursor-style')?.remove();
            const style = document.createElement('style');
            style.id = 'gif-cursor-style';
            style.textContent = `
                #gif-cursor {
                    position: fixed;
                    z-index: 2147483647;
                    pointer-events: none;
                    left: 0;
                    top: 0;
                    transform: translate(-3px, -3px);
                }
                #gif-cursor svg {
                    display: block;
                    width: 28px;
                    height: 36px;
                    filter: drop-shadow(1px 2px 2px rgba(0, 0, 0, .85));
                }
                #gif-cursor-ring {
                    position: absolute;
                    left: -5px;
                    top: -5px;
                    width: 34px;
                    height: 34px;
                    border: 3px solid #76a5ff;
                    border-radius: 50%;
                    opacity: 0;
                }
                #gif-cursor-ring.is-click {
                    opacity: 1;
                }
                #gif-cursor-label {
                    position: absolute;
                    left: 25px;
                    top: 22px;
                    max-width: 240px;
                    padding: 4px 8px;
                    color: #f5f7ff;
                    background: #172447;
                    border: 1px solid #5f84e8;
                    border-radius: 5px;
                    box-shadow: 0 3px 12px rgba(0, 0, 0, .45);
                    font: 600 13px/1.2 system-ui, sans-serif;
                    white-space: nowrap;
                }
            `;
            document.head.appendChild(style);
            const cursor = document.createElement('div');
            cursor.id = 'gif-cursor';
            cursor.innerHTML = `
                <div id="gif-cursor-ring"></div>
                <svg viewBox="0 0 28 36" aria-hidden="true">
                    <path d="M2 2 L2 30 L10 22 L16 34 L21 31 L15 19 L26 19 Z"
                          fill="#ffffff" stroke="#10131c" stroke-width="2" stroke-linejoin="round" />
                </svg>
                <span id="gif-cursor-label"></span>
            `;
            document.body.appendChild(cursor);
            window.__gifCursor = {
                set(x, y, label) {
                    cursor.style.left = `${x}px`;
                    cursor.style.top = `${y}px`;
                    const labelElement = document.getElementById('gif-cursor-label');
                    labelElement.textContent = label;
                    if (!label) {
                        labelElement.style.display = 'none';
                        document.getElementById('gif-cursor-ring').classList.remove('is-click');
                        return;
                    }
                    labelElement.style.display = 'block';
                    labelElement.style.left = '25px';
                    labelElement.style.top = '22px';
                    const labelBox = labelElement.getBoundingClientRect();
                    const padding = 8;
                    const left = x + 25 + labelBox.width > window.innerWidth - padding
                        ? -labelBox.width - 10
                        : 25;
                    const top = y + 22 + labelBox.height > window.innerHeight - padding
                        ? -labelBox.height - 10
                        : 22;
                    labelElement.style.left = `${left}px`;
                    labelElement.style.top = `${top}px`;
                    document.getElementById('gif-cursor-ring').classList.remove('is-click');
                },
                click() {
                    document.getElementById('gif-cursor-ring').classList.add('is-click');
                }
            };
        }
        """
    )
    install_progress(page)


def wait_for_ready(page, heading=None):
    try:
        page.wait_for_load_state("networkidle", timeout=15_000)
    except Exception:
        pass
    expect(page.locator('[data-pw="blazor-ready"]')).to_be_attached(timeout=30_000)
    if heading:
        expect(page.get_by_role("heading", name=heading, exact=True)).to_be_visible(timeout=30_000)
    page.wait_for_timeout(600)


def open_page(page, path, heading=None):
    page.goto(f"{BASE_URL}{path}", wait_until="domcontentloaded")
    wait_for_ready(page, heading)
    install_cursor(page)


def capture_hold(page, frame_dir, frame_number, seconds=2.0):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        page.screenshot(
            path=str(frame_dir / f"frame-{frame_number[0]:03d}.png"),
            animations="disabled",
        )
        frame_number[0] += 1
        page.wait_for_timeout(int(FRAME_INTERVAL * 1000))


def move_cursor(page, locator, label, frame_dir, frame_number, cursor_position, duration=0.65):
    box = locator.bounding_box()
    if box is None:
        raise RuntimeError(f"Cannot locate cursor target for {label}.")
    target = (box["x"] + box["width"] / 2, box["y"] + box["height"] / 2)
    if cursor_position[0] is None:
        page.evaluate(
            "({ x, y, label }) => window.__gifCursor.set(x, y, label)",
            {"x": target[0], "y": target[1], "label": label},
        )
        cursor_position[0] = target
        return

    start = cursor_position[0]
    steps = max(1, round(duration / FRAME_INTERVAL))
    for step in range(1, steps + 1):
        progress = step / steps
        eased = progress * progress * (3 - 2 * progress)
        x = start[0] + (target[0] - start[0]) * eased
        y = start[1] + (target[1] - start[1]) * eased
        page.evaluate(
            "({ x, y, label }) => window.__gifCursor.set(x, y, label)",
            {"x": x, "y": y, "label": "" if step < steps else label},
        )
        page.screenshot(
            path=str(frame_dir / f"frame-{frame_number[0]:03d}.png"),
            animations="disabled",
        )
        frame_number[0] += 1
        page.wait_for_timeout(int(FRAME_INTERVAL * 1000))
    cursor_position[0] = target


def show_click(page, locator, label, frame_dir, frame_number, cursor_position):
    move_cursor(page, locator, label, frame_dir, frame_number, cursor_position)
    capture_hold(page, frame_dir, frame_number, seconds=0.75)
    page.evaluate("() => window.__gifCursor.click()")
    capture_hold(page, frame_dir, frame_number, seconds=0.5)
    locator.click()


def install_edge_preview(page):
    page.evaluate(
        """
        () => {
            document.getElementById('gif-edge-preview')?.remove();
            const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.id = 'gif-edge-preview';
            svg.setAttribute('viewBox', '0 0 1440 900');
            svg.style.cssText = 'position:fixed;inset:0;width:100vw;height:100vh;z-index:2147483646;pointer-events:none;display:block;';
            const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
            line.id = 'gif-edge-preview-line';
            line.setAttribute('stroke', '#76a5ff');
            line.setAttribute('stroke-width', '4');
            line.setAttribute('stroke-dasharray', '10 7');
            line.setAttribute('stroke-linecap', 'round');
            svg.appendChild(line);
            document.body.appendChild(svg);
        }
        """
    )


def set_edge_preview(page, source_point, target_point, visible=True):
    page.evaluate(
        "({ source, target, visible }) => { const line = document.getElementById('gif-edge-preview-line'); const svg = document.getElementById('gif-edge-preview'); if (!line || !svg) return; line.setAttribute('x1', source.x); line.setAttribute('y1', source.y); line.setAttribute('x2', target.x); line.setAttribute('y2', target.y); svg.style.display = visible ? 'block' : 'none'; }",
        {"source": {"x": source_point[0], "y": source_point[1]}, "target": {"x": target_point[0], "y": target_point[1]}, "visible": visible},
    )


def animate_edge_preview(page, source, target, frame_dir, frame_number, cursor_position):
    source_box = source.bounding_box()
    target_box = target.bounding_box()
    if source_box is None or target_box is None:
        raise RuntimeError("Cannot locate graph edge endpoints.")

    source_point = (
        source_box["x"] + source_box["width"] / 2,
        source_box["y"] + source_box["height"] / 2,
    )
    target_point = (
        target_box["x"] + target_box["width"] / 2,
        target_box["y"] + target_box["height"] / 2,
    )
    move_cursor(page, source, "Drag from new node", frame_dir, frame_number, cursor_position)
    capture_hold(page, frame_dir, frame_number, seconds=0.75)
    install_edge_preview(page)

    steps = max(12, round(1.0 / FRAME_INTERVAL))
    for step in range(1, steps + 1):
        progress = step / steps
        eased = progress * progress * (3 - 2 * progress)
        x = source_point[0] + (target_point[0] - source_point[0]) * eased
        y = source_point[1] + (target_point[1] - source_point[1]) * eased
        set_edge_preview(page, source_point, (x, y))
        page.evaluate("({ x, y }) => window.__gifCursor.set(x, y, 'Connect to done')", {"x": x, "y": y})
        page.screenshot(
            path=str(frame_dir / f"frame-{frame_number[0]:03d}.png"),
            animations="disabled",
        )
        frame_number[0] += 1
        page.wait_for_timeout(int(FRAME_INTERVAL * 1000))
    set_edge_preview(page, source_point, target_point, visible=False)
    cursor_position[0] = target_point


def clean_frames(frame_dir):
    frame_dir.mkdir(parents=True, exist_ok=True)
    for old_frame in frame_dir.glob("frame-*.png"):
        old_frame.unlink()


def capture_team_room(page, request):
    frame_dir = TEAM_FRAME_DIR
    clean_frames(frame_dir)
    frame_number = [0]
    cursor_position = [None]
    mission_id = "demo-team-room"
    orchestrator_id = f"{mission_id}-orchestrator"
    spec_id = f"{mission_id}-spec"
    dev_id = f"{mission_id}-dev"

    seed(request, {
        "mission": {
            "missionId": mission_id,
            "goal": "Implement input validation and verify it with tests.",
            "targetKind": "Team",
            "targetName": "demo-team",
            "teamName": "demo-team",
            "status": "Running",
            "budget": {"costUsedUsd": 1.24, "iterationsUsed": 1, "maxIterations": 5, "maxConcurrentAgents": 4},
        },
        "agents": [
            {"instanceId": orchestrator_id, "agentName": "orchestrator-agent", "role": "Orchestrator", "instanceNo": 0, "state": "Thinking", "modelName": "gpt-4.1"},
            {"instanceId": spec_id, "agentName": "spec-research-agent", "role": "Member", "instanceNo": 1, "state": "Completed", "modelName": "gpt-4.1"},
            {"instanceId": dev_id, "agentName": "dev-agent", "role": "Member", "instanceNo": 2, "state": "AwaitingReply", "awaitingInstanceId": spec_id, "modelName": "gpt-4.1"},
        ],
        "messages": [{
            "missionId": mission_id,
            "messageId": f"{mission_id}-delegate",
            "senderInstanceId": orchestrator_id,
            "recipientInstanceId": dev_id,
            "kind": "Delegate",
            "body": "Please review the request and propose an implementation plan.",
            "delegationDepth": 1,
            "secondsAgo": 30,
        }],
    })

    open_page(page, "/missions/new", "New mission")
    set_progress(page, 0, 6, "Enter mission goal")
    goal_field = page.locator("#mission-goal")
    move_cursor(page, goal_field, "Enter mission goal", frame_dir, frame_number, cursor_position)
    capture_hold(page, frame_dir, frame_number, seconds=0.75)
    goal_field.click()
    page.keyboard.type("Implement input validation and verify it with tests.", delay=12)
    page.keyboard.press("Tab")
    capture_hold(page, frame_dir, frame_number, seconds=1.0)

    target_kind = page.locator("#target-kind")
    target_name = page.locator("#target-name")
    set_progress(page, 1, 6, "Select Team")
    move_cursor(page, target_kind, "Select Team", frame_dir, frame_number, cursor_position)
    capture_hold(page, frame_dir, frame_number, seconds=0.75)
    target_kind.select_option("Team")
    target_name.select_option("demo-team")
    move_cursor(page, target_name, "Select demo-team", frame_dir, frame_number, cursor_position)
    capture_hold(page, frame_dir, frame_number, seconds=1.0)

    set_progress(page, 2, 6, "Send mission")
    submit_button = page.get_by_role("button", name="ミッションを開始", exact=True)
    show_click(page, submit_button, "Send mission", frame_dir, frame_number, cursor_position)
    wait_for_ready(page, "Team Room")
    install_cursor(page)
    cursor_position[0] = None
    set_progress(page, 3, 6, "Delegation")

    message_rows = page.locator('[data-pw="message"]')
    move_cursor(page, message_rows.last, "Delegation", frame_dir, frame_number, cursor_position)
    capture_hold(page, frame_dir, frame_number)

    for message, label in [
        ({"messageId": f"{mission_id}-question", "senderInstanceId": dev_id, "recipientInstanceId": spec_id, "kind": "Question", "body": "What should happen when the input is empty?", "delegationDepth": 2, "secondsAgo": 20}, "Question"),
        ({"messageId": f"{mission_id}-answer", "senderInstanceId": spec_id, "recipientInstanceId": dev_id, "kind": "Answer", "body": "Empty input should be rejected with a clear validation message.", "delegationDepth": 2, "inputRefs": "requirements.md#3.2", "secondsAgo": 10}, "Answer"),
        ({"messageId": f"{mission_id}-report", "senderInstanceId": dev_id, "recipientInstanceId": orchestrator_id, "kind": "Report", "body": "The implementation is ready for verification.", "delegationDepth": 1, "secondsAgo": 5}, "Report"),
    ]:
        message["missionId"] = mission_id
        seed(request, {"messages": [message]})
        open_page(page, f"/missions/{mission_id}", "Team Room")
        cursor_position[0] = None
        set_progress(page, {"Question": 4, "Answer": 5, "Report": 6}[label], 6, label)
        message_rows = page.locator('[data-pw="message"]')
        move_cursor(page, message_rows.last, label, frame_dir, frame_number, cursor_position)
        capture_hold(page, frame_dir, frame_number)

    show_click(page, message_rows.last, "Open report details", frame_dir, frame_number, cursor_position)
    set_progress(page, 6, 6, "Report details")
    capture_hold(page, frame_dir, frame_number, seconds=1.5)


def capture_approvals(page, request):
    frame_dir = APPROVAL_FRAME_DIR
    clean_frames(frame_dir)
    frame_number = [0]
    cursor_position = [None]
    suffix = int(time.time())
    create_approval(request, f"run-shell-{suffix}", "shell", "dotnet test WorkAgents.sln --configuration Release")
    create_approval(request, f"run-file-{suffix}", "file.write", "Write review-summary.md to the artifacts directory")

    open_page(page, "/approvals", "Approvals")
    set_progress(page, 1, 4, "Review requests")
    rows = page.locator('[data-pw="approval-row"]')
    expect(rows).to_have_count(2, timeout=30_000)
    file_row = rows.filter(has_text="Write review-summary.md")
    show_click(page, file_row, "Select file write request", frame_dir, frame_number, cursor_position)
    set_progress(page, 2, 4, "Select request")
    expect(page.get_by_role("region", name="承認要求の詳細")).to_contain_text("file.write")
    capture_hold(page, frame_dir, frame_number)

    approve_button = page.get_by_role("button", name="Approve & resume", exact=True)
    show_click(page, approve_button, "Approve & resume", frame_dir, frame_number, cursor_position)
    set_progress(page, 3, 4, "Approve & resume")
    expect(rows).to_have_count(1, timeout=30_000)
    capture_hold(page, frame_dir, frame_number)

    approve_button = page.get_by_role("button", name="Approve & resume", exact=True)
    show_click(page, approve_button, "Approve & resume", frame_dir, frame_number, cursor_position)
    set_progress(page, 4, 4, "Complete")
    expect(rows).to_have_count(0, timeout=30_000)
    capture_hold(page, frame_dir, frame_number, seconds=2.5)


def capture_graph(page):
    frame_dir = GRAPH_FRAME_DIR
    clean_frames(frame_dir)
    frame_number = [0]
    cursor_position = [None]

    open_page(page, "/graphs", "Graph Studio")
    set_progress(page, 0, 6, "Open graph")
    graph_item = page.locator('[data-pw="graph-list-item"]').filter(has_text="demo-graph")
    show_click(page, graph_item, "Open demo-graph", frame_dir, frame_number, cursor_position)
    wait_for_ready(page)
    install_cursor(page)
    cursor_position[0] = None
    set_progress(page, 1, 6, "Graph loaded")
    canvas = page.locator('[data-pw="graph-canvas"]')
    expect(canvas).to_contain_text("start")
    expect(page.locator('[data-pw="graph-node"]')).to_have_count(9)
    move_cursor(page, canvas, "Graph definition", frame_dir, frame_number, cursor_position)
    capture_hold(page, frame_dir, frame_number, seconds=1.75)

    verify_node = page.locator('[data-pw="graph-node"]').filter(has_text="verify")
    verify_node.scroll_into_view_if_needed()
    page.wait_for_timeout(500)
    show_click(page, verify_node, "Select loop node", frame_dir, frame_number, cursor_position)
    set_progress(page, 2, 6, "Select loop node")
    expect(page.locator('[data-pw="schema-field-node.kind"]')).to_have_value("loop")
    capture_hold(page, frame_dir, frame_number, seconds=1.75)

    validate_button = page.locator('[data-pw="graph-validate"]')
    show_click(page, validate_button, "Validate graph", frame_dir, frame_number, cursor_position)
    set_progress(page, 3, 6, "Validate graph")
    expect(page.locator('[data-pw="diagnostics-valid"]')).to_be_visible()
    capture_hold(page, frame_dir, frame_number, seconds=1.75)

    yaml_button = page.locator('[data-pw="graph-show-yaml"]')
    show_click(page, yaml_button, "Show YAML", frame_dir, frame_number, cursor_position)
    set_progress(page, 4, 6, "Show YAML")
    expect(page.locator('[data-pw="graph-yaml"]')).to_contain_text('name: "demo-graph"')
    capture_hold(page, frame_dir, frame_number, seconds=1.75)

    add_node_button = page.locator('[data-pw="graph-add-node"]')
    show_click(page, add_node_button, "Add node", frame_dir, frame_number, cursor_position)
    set_progress(page, 5, 6, "Add node")
    capture_hold(page, frame_dir, frame_number, seconds=0.75)
    new_node = page.locator('[data-pw="graph-node"][data-node-id="node"]')
    expect(new_node).to_have_count(1)
    expect(page.locator('[data-pw="graph-dirty"]')).to_be_visible()
    new_node.scroll_into_view_if_needed()
    page.wait_for_timeout(500)
    move_cursor(page, new_node, "New node added", frame_dir, frame_number, cursor_position)
    capture_hold(page, frame_dir, frame_number, seconds=2.25)

    source_handle = page.locator('[data-pw="graph-edge-handle"][data-node-id="node"]')
    target_node = page.locator('[data-pw="graph-node"][data-node-id="done"]')
    set_progress(page, 6, 6, "Connect to done")
    animate_edge_preview(page, source_handle, target_node, frame_dir, frame_number, cursor_position)

    edge_tab = page.locator('[data-pw="inspector-tab-edges"]')
    edge_tab.click()
    add_edge_button = page.locator('[data-pw="graph-add-edge"]')
    add_edge_button.click()
    edge_cards = page.locator('[data-pw="edge-card"]')
    expect(edge_cards).to_have_count(12, timeout=30_000)
    last_edge = edge_cards.last
    to_field = last_edge.locator('[data-pw="schema-field-edge.to"]')
    to_field.select_option("done")
    expect(to_field).to_have_value("done")
    expect(page.locator('[data-pw="edge-card"]')).to_have_count(12, timeout=30_000)

    page.locator('[data-pw="inspector-tab-node"]').click()
    page.evaluate("window.scrollTo(0, 0)")
    page.locator('[data-pw="graph-canvas"]').evaluate("element => { element.scrollTop = 0; }")
    page.wait_for_timeout(500)
    new_node.scroll_into_view_if_needed()
    page.locator('[data-pw="graph-canvas"]').evaluate(
        "element => { element.scrollLeft = Math.min(element.scrollWidth - element.clientWidth, element.scrollLeft + 180); }"
    )
    page.wait_for_timeout(500)
    move_cursor(page, new_node, "Connected to done", frame_dir, frame_number, cursor_position)
    capture_hold(page, frame_dir, frame_number, seconds=2.0)


def main():
    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(headless=True)
        context = browser.new_context(viewport={"width": 1440, "height": 900}, locale="en-US", timezone_id="UTC")
        request = playwright.request.new_context(base_url=BASE_URL)
        page = context.new_page()
        try:
            capture_team_room(page, request)
            capture_approvals(page, request)
            capture_graph(page)
        finally:
            request.dispose()
            context.close()
            browser.close()


if __name__ == "__main__":
    main()
