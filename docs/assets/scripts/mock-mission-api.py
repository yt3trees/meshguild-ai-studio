import json
from http.server import BaseHTTPRequestHandler, HTTPServer


class MissionApiHandler(BaseHTTPRequestHandler):
    def do_POST(self):
        if self.path != "/missions":
            self.send_error(404)
            return

        length = int(self.headers.get("Content-Length", "0"))
        self.rfile.read(length)
        body = json.dumps({
            "missionId": "demo-team-room",
            "status": "Queued",
            "queuedReason": None,
            "queuePosition": None,
        }).encode("utf-8")
        self.send_response(202)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):
        return


HTTPServer(("127.0.0.1", 5050), MissionApiHandler).serve_forever()
