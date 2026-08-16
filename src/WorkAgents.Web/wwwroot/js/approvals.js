// WorkAgents approval notification helper (M3.5/5.13.4).
// Blazor Server から JS interop で呼び出す。ブラウザ Notification API を経由し、
// /approvals を開いていなくても承認要求が届いた時にデスクトップ通知を出す。
window.workAgentsNotifications = (function () {
    function ensurePermission() {
        try {
            if (!("Notification" in window)) return Promise.resolve("unsupported");
            if (Notification.permission === "granted") return Promise.resolve("granted");
            if (Notification.permission === "denied") return Promise.resolve("denied");
            return Notification.requestPermission().then(function (v) { return v; });
        } catch (e) {
            return Promise.resolve("error");
        }
    }

    function show(title, body, approvalId) {
        try {
            if (!("Notification" in window)) return;
            if (Notification.permission !== "granted") return;
            var n = new Notification(title, {
                body: body || "",
                tag: approvalId || "workagents-approval",
                //lang: "ja",
                //icon: "/favicon.png"
            });
            n.onclick = function () {
                try { window.focus(); } catch (e) {}
                try { n.close(); } catch (e) {}
                try {
                    history.pushState({}, "", "/approvals");
                    var ev = new PopStateEvent("popstate", { state: {} });
                    window.dispatchEvent(ev);
                } catch (e) {}
            };
        } catch (e) {
            // swallow
        }
    }

    return {
        ensurePermission: ensurePermission,
        show: show
    };
})();