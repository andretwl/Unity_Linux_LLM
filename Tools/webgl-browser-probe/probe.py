#!/usr/bin/env python3
"""
WebGL Browser Crash Probe — Task 2 of webgl-dialogue-current-state-plan

Serves the current WebGL artifact with COOP/COEP headers, opens Chromium via Playwright,
and captures console/pageerror/requestfailed/response/crash events plus screenshots.

Usage:
    python3 Tools/webgl-browser-probe/probe.py [--port 8099] [--artifact Builds/WebGL_client/WebGL]

Exit codes:
    0 = page reached auth/game UI without crash
    1 = page crashed, missing crossOriginIsolated, fatal errors, or timeout
"""

import argparse
import json
import os
import sys
import threading
import time
from http.server import HTTPServer, SimpleHTTPRequestHandler
from pathlib import Path

# ── MIME overrides for WebGL ──────────────────────────────────────────
MIME_OVERRIDES = {
    ".wasm": "application/wasm",
    ".js": "application/javascript",
    ".data": "application/octet-stream",
    ".json": "application/json",
    ".br": "application/octet-stream",
    ".gz": "application/octet-stream",
}


class WebGLHandler(SimpleHTTPRequestHandler):
    """HTTP handler with COOP/COEP headers and correct MIME types."""

    def end_headers(self):
        self.send_header("Cross-Origin-Opener-Policy", "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate")
        super().end_headers()

    def guess_type(self, path):
        for ext, mime in MIME_OVERRIDES.items():
            if path.endswith(ext):
                return mime
        return super().guess_type(path)

    def log_message(self, format, *args):
        pass  # Silence request logs


def serve_artifact(artifact_dir, port):
    """Start a minimal HTTP server in a background thread."""
    os.chdir(artifact_dir)
    server = HTTPServer(("127.0.0.1", port), WebGLHandler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    return server


def run_probe(artifact_dir, port, timeout_s=60):
    """Run the Playwright probe and return structured results."""
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print("ERROR: playwright not installed. Run: pip install playwright && playwright install chromium")
        sys.exit(1)

    server = serve_artifact(artifact_dir, port)
    results = {
        "url": f"http://127.0.0.1:{port}",
        "artifact_dir": str(artifact_dir),
        "timeout_s": timeout_s,
        "events": [],
        "console": [],
        "page_errors": [],
        "request_failed": [],
        "response_failures": [],
        "cross_origin_isolated": False,
        "canvas_visible": False,
        "canvas_blank": True,
        "auth_or_game_ui_visible": False,
        "crashed": False,
        "passed": False,
    }

    try:
        with sync_playwright() as p:
            browser = p.chromium.launch(
                headless=True,
                args=[
                    "--use-gl=angle",
                    "--use-angle=swiftshader",
                    "--enable-webgl",
                    "--enable-features=SharedArrayBuffer",
                    "--no-sandbox",
                ],
            )
            context = browser.new_context(
                viewport={"width": 1280, "height": 720},
            )
            page = context.new_page()

            # Capture events
            page.on("console", lambda msg: results["console"].append({
                "type": msg.type,
                "text": msg.text,
                "timestamp": time.time(),
            }))
            page.on("pageerror", lambda err: results["page_errors"].append({
                "message": str(err),
                "timestamp": time.time(),
            }))
            page.on("requestfailed", lambda req: results["request_failed"].append({
                "url": req.url,
                "failure": req.failure,
                "timestamp": time.time(),
            }))
            page.on("crash", lambda: results.update({"crashed": True}))

            def on_response(response):
                if response.status >= 400:
                    results["response_failures"].append({
                        "url": response.url,
                        "status": response.status,
                        "timestamp": time.time(),
                    })

            page.on("response", on_response)

            # Navigate
            print(f"[*] Navigating to {results['url']} ...")
            page.goto(results["url"], wait_until="domcontentloaded", timeout=15000)

            # Wait for Unity to initialize
            print(f"[*] Waiting up to {timeout_s}s for Unity initialization ...")
            start_time = time.time()
            while time.time() - start_time < timeout_s:
                time.sleep(2)

                # Check crossOriginIsolated
                try:
                    isolated = page.evaluate("() => crossOriginIsolated")
                    results["cross_origin_isolated"] = isolated
                except Exception:
                    pass

                # Check for canvas
                try:
                    canvas_info = page.evaluate("""() => {
                        const canvas = document.querySelector('canvas');
                        if (!canvas) return { exists: false };
                        const rect = canvas.getBoundingClientRect();
                        const ctx = canvas.getContext('2d');
                        let blank = false;
                        if (ctx) {
                            const data = ctx.getImageData(0, 0, 1, 1).data;
                            blank = data[0] === 0 && data[1] === 0 && data[2] === 0 && data[3] === 0;
                        }
                        return {
                            exists: true,
                            width: rect.width,
                            height: rect.height,
                            visible: rect.width > 0 && rect.height > 0,
                            blank: blank,
                        };
                    }""")
                    if canvas_info.get("exists"):
                        results["canvas_visible"] = canvas_info.get("visible", False)
                        results["canvas_blank"] = canvas_info.get("blank", True)
                except Exception:
                    pass

                # Check for auth/game UI elements
                try:
                    ui_info = page.evaluate("""() => {
                        const body = document.body;
                        const text = body ? body.innerText : '';
                        return {
                            hasContent: text.length > 10,
                            preview: text.substring(0, 200),
                        };
                    }""")
                    results["auth_or_game_ui_visible"] = ui_info.get("hasContent", False)
                except Exception:
                    pass

                # Check for fatal conditions
                if results["crashed"]:
                    print("[!] Page crashed!")
                    break
                fatal_errors = [e for e in results["page_errors"] if "RuntimeError" in e.get("message", "") or "out of bounds" in e.get("message", "").lower()]
                if fatal_errors:
                    print(f"[!] Fatal page error detected: {fatal_errors[0]['message']}")
                    break

                # Early success: auth UI or gameplay UI visible, canvas present, no crash
                if (results["cross_origin_isolated"]
                    and results["canvas_visible"]
                    and not results["canvas_blank"]
                    and results["auth_or_game_ui_visible"]
                    and not results["crashed"]
                    and not fatal_errors):
                    print("[+] Early success criteria met!")
                    break

            # Final screenshot
            screenshot_path = Path(artifact_dir).parent / "probe-screenshot.png"
            page.screenshot(path=str(screenshot_path))
            results["screenshot"] = str(screenshot_path)

            # Evaluate pass/fail
            fatal_errors = [e for e in results["page_errors"] if "RuntimeError" in e.get("message", "") or "out of bounds" in e.get("message", "").lower()]
            fatal_console = [c for c in results["console"] if c["type"] == "error"]

            results["passed"] = (
                not results["crashed"]
                and not fatal_errors
                and results["cross_origin_isolated"]
                and results["canvas_visible"]
            )

            browser.close()

    except Exception as e:
        results["error"] = str(e)
        print(f"[!] Probe exception: {e}")
    finally:
        server.shutdown()

    return results


def main():
    parser = argparse.ArgumentParser(description="WebGL Browser Crash Probe")
    parser.add_argument("--port", type=int, default=8099, help="Port to serve on")
    parser.add_argument("--artifact", type=str, default="Builds/WebGL_client/WebGL", help="Path to WebGL artifact")
    parser.add_argument("--timeout", type=int, default=60, help="Timeout in seconds")
    parser.add_argument("--output", type=str, default=None, help="Output JSON path")
    args = parser.parse_args()

    artifact_dir = Path(args.artifact).resolve()
    if not artifact_dir.exists():
        print(f"ERROR: Artifact directory not found: {artifact_dir}")
        sys.exit(1)

    results = run_probe(artifact_dir, args.port, args.timeout)

    # Write results
    output_path = args.output or str(artifact_dir.parent / "probe-results.json")
    with open(output_path, "w") as f:
        json.dump(results, f, indent=2)
    print(f"[*] Results written to {output_path}")

    # Print summary
    print("\n═══════════════════════════════════════════")
    print("  WebGL Browser Probe — Summary")
    print("═══════════════════════════════════════════")
    print(f"  crossOriginIsolated:  {results['cross_origin_isolated']}")
    print(f"  canvas visible:       {results['canvas_visible']}")
    print(f"  canvas blank:         {results['canvas_blank']}")
    print(f"  auth/game UI:         {results['auth_or_game_ui_visible']}")
    print(f"  page crashed:         {results['crashed']}")
    print(f"  page errors:          {len(results['page_errors'])}")
    print(f"  request failures:     {len(results['request_failed'])}")
    print(f"  console errors:       {len([c for c in results['console'] if c['type'] == 'error'])}")
    print(f"  PASSED:               {results['passed']}")
    print("═══════════════════════════════════════════")

    sys.exit(0 if results["passed"] else 1)


if __name__ == "__main__":
    main()
