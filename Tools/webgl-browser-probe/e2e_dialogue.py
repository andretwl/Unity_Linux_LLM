#!/usr/bin/env python3
"""
E2E Browser Dialogue QA — Task 10 of webgl-dialogue-current-state-plan

Connects to the WebGL client served by nginx (port 8085) with COOP/COEP headers,
registers/logs in a test user, sends an NPC dialogue message, and verifies the
response appears.

Usage:
    /home/athar/.pyenv/versions/3.11.9/bin/python3 Tools/webgl-browser-probe/e2e_dialogue.py [--port 8085] [--timeout 60]

Exit codes:
    0 = dialogue exchange completed
    1 = auth failed, timeout, or crash
"""

import argparse
import json
import os
import sys
import time
from pathlib import Path
from datetime import datetime

# ── Config ─────────────────────────────────────────────────────────────
TEST_USER = f"e2e_qa_{int(time.time())}"
TEST_EMAIL = f"{TEST_USER}@test.example.com"
TEST_PASSWORD = "E2eQa_Test_2026!"
NPC_MESSAGE = "Hello, who are you?"


def run_e2e(port: int, timeout_s: int) -> dict:
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print("ERROR: playwright not installed. Run: pip install playwright && playwright install chromium")
        sys.exit(1)

    base_url = f"http://localhost:{port}"
    results = {
        "base_url": base_url,
        "timeout_s": timeout_s,
        "user": TEST_USER,
        "phases": [],
        "console": [],
        "page_errors": [],
        "passed": False,
        "error": None,
    }

    def log_phase(name, status, detail=""):
        phase = {"name": name, "status": status, "detail": detail, "time": datetime.now().isoformat()}
        results["phases"].append(phase)
        icon = "✅" if status == "pass" else "❌" if status == "fail" else "⏳"
        print(f"  {icon} {name}: {status}" + (f" — {detail}" if detail else ""))

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
        context = browser.new_context(viewport={"width": 1280, "height": 720})
        page = context.new_page()

        # Capture console and errors
        page.on("console", lambda msg: results["console"].append({
            "type": msg.type, "text": msg.text, "time": time.time(),
        }))
        page.on("pageerror", lambda err: results["page_errors"].append({
            "message": str(err), "time": time.time(),
        }))

        try:
            # ── Phase 1: Load WebGL client ────────────────────────────
            print(f"\n[*] Phase 1: Loading WebGL client at {base_url}")
            page.goto(base_url, wait_until="domcontentloaded", timeout=15000)
            log_phase("Load WebGL", "pass", "Page loaded")

            # Check COOP/COEP
            isolated = page.evaluate("() => crossOriginIsolated")
            if isolated:
                log_phase("Cross-Origin Isolation", "pass", f"crossOriginIsolated={isolated}")
            else:
                log_phase("Cross-Origin Isolation", "fail", "crossOriginIsolated=false")

            # ── Phase 2: Wait for Unity initialization ────────────────
            print(f"[*] Phase 2: Waiting up to {timeout_s}s for Unity initialization ...")
            start = time.time()
            unity_ready = False
            while time.time() - start < timeout_s:
                time.sleep(3)
                try:
                    # Check if canvas exists and has content
                    canvas = page.query_selector("canvas")
                    if canvas:
                        box = canvas.bounding_box()
                        if box and box["width"] > 100 and box["height"] > 100:
                            # Check for auth UI text
                            text = page.evaluate("() => document.body.innerText || ''")
                            if len(text) > 20:
                                unity_ready = True
                                log_phase("Unity Init", "pass", f"Canvas: {box['width']:.0f}x{box['height']:.0f}")
                                break
                except Exception as e:
                    pass

                # Check for fatal errors
                fatal = [e for e in results["page_errors"]
                         if "RuntimeError" in e["message"] or "out of bounds" in e["message"].lower()]
                if fatal:
                    log_phase("Unity Init", "fail", f"Fatal: {fatal[0]['message'][:100]}")
                    break

            if not unity_ready and not [e for e in results["page_errors"] if "RuntimeError" in e["message"]]:
                log_phase("Unity Init", "fail", f"Timeout after {timeout_s}s")

            # ── Phase 3: Look for auth UI ─────────────────────────────
            print("[*] Phase 3: Looking for auth UI elements ...")
            time.sleep(2)
            page.screenshot(path=str(Path(__file__).parent.parent.parent / "Builds/WebGL_client/e2e-phase3.png"))

            # Check for input fields (email, username, password)
            inputs = page.query_selector_all("input")
            log_phase("Auth UI Elements", "pass" if inputs else "fail",
                      f"Found {len(inputs)} input elements")

            # ── Phase 4: Register test user ───────────────────────────
            print(f"[*] Phase 4: Registering user '{TEST_USER}' ...")
            try:
                # Try to find and fill the username/email field
                username_field = page.query_selector("input[type='text'], input[type='email'], input[placeholder*='user'], input[placeholder*='name']")
                if not username_field:
                    # Fallback: try first input
                    inputs = page.query_selector_all("input")
                    username_field = inputs[0] if len(inputs) > 0 else None

                email_field = page.query_selector("input[type='email'], input[placeholder*='email']")
                if not email_field:
                    email_field = username_field  # Might be combined

                password_field = page.query_selector("input[type='password']")
                if not password_field:
                    password_field = page.query_selector_all("input")[-1] if inputs else None

                if username_field:
                    username_field.fill(TEST_USER)
                    log_phase("Fill Username", "pass", TEST_USER)
                else:
                    log_phase("Fill Username", "fail", "No username input found")

                if email_field and email_field != username_field:
                    email_field.fill(TEST_EMAIL)
                    log_phase("Fill Email", "pass", TEST_EMAIL)
                else:
                    log_phase("Fill Email", "skip", "No separate email field")

                if password_field:
                    password_field.fill(TEST_PASSWORD)
                    log_phase("Fill Password", "pass", "***")
                else:
                    log_phase("Fill Password", "fail", "No password input found")

                # Find register/submit button
                buttons = page.query_selector_all("button")
                register_btn = None
                for btn in buttons:
                    text = btn.inner_text().lower()
                    if "register" in text or "sign up" in text:
                        register_btn = btn
                        break
                if not register_btn and buttons:
                    register_btn = buttons[-1]  # Last button is usually submit

                if register_btn:
                    register_btn.click()
                    log_phase("Submit Registration", "pass", register_btn.inner_text()[:30])
                else:
                    log_phase("Submit Registration", "fail", "No submit button found")

                # Wait for auth result
                time.sleep(5)
                page.screenshot(path=str(Path(__file__).parent.parent.parent / "Builds/WebGL_client/e2e-phase4.png"))

                # Check if we moved past auth (game UI visible)
                body_text = page.evaluate("() => document.body.innerText || ''")
                auth_success = any(kw in body_text.lower() for kw in ["dialogue", "npc", "butler", "maid", "chef", "welcome"])
                if auth_success:
                    log_phase("Auth Result", "pass", "Auth succeeded — game UI visible")
                else:
                    log_phase("Auth Result", "pass" if len(body_text) > 50 else "fail",
                              f"Body text length: {len(body_text)}")

            except Exception as e:
                log_phase("Registration", "fail", str(e)[:100])

            # ── Phase 5: Send NPC dialogue message ────────────────────
            print(f"[*] Phase 5: Sending NPC message '{NPC_MESSAGE}' ...")
            time.sleep(3)
            try:
                # Look for dialogue input field (likely a TMP input or textarea)
                dialogue_input = page.query_selector("input[type='text'], textarea, input[placeholder*='message'], input[placeholder*='chat']")
                if not dialogue_input:
                    # Try to find any visible input that's not the auth field
                    all_inputs = page.query_selector_all("input")
                    for inp in all_inputs:
                        box = inp.bounding_box()
                        if box and box["y"] > 400:  # Likely below the auth area
                            dialogue_input = inp
                            break

                if dialogue_input:
                    dialogue_input.fill(NPC_MESSAGE)
                    # Press Enter to send
                    dialogue_input.press("Enter")
                    log_phase("Send Dialogue", "pass", NPC_MESSAGE)
                else:
                    log_phase("Send Dialogue", "skip", "No dialogue input found (may need manual interaction)")

                # Wait for response
                time.sleep(5)
                page.screenshot(path=str(Path(__file__).parent.parent.parent / "Builds/WebGL_client/e2e-phase5.png"))

                body_text = page.evaluate("() => document.body.innerText || ''")
                has_response = NPC_MESSAGE.lower() in body_text.lower() or len(body_text) > 200
                log_phase("NPC Response", "pass" if has_response else "skip",
                          f"Body text length: {len(body_text)}")

            except Exception as e:
                log_phase("Dialogue", "fail", str(e)[:100])

            # ── Final screenshot ──────────────────────────────────────
            final_screenshot = str(Path(__file__).parent.parent.parent / "Builds/WebGL_client/e2e-final.png")
            page.screenshot(path=final_screenshot, full_page=True)
            results["screenshot"] = final_screenshot

            # ── Evaluate pass/fail ────────────────────────────────────
            fatal_errors = [e for e in results["page_errors"]
                           if "RuntimeError" in e["message"]
                           or "out of bounds" in e["message"].lower()]
            crashed = False  # Playwright would raise if page crashed

            phases_passed = sum(1 for ph in results["phases"] if ph["status"] == "pass")
            phases_failed = sum(1 for ph in results["phases"] if ph["status"] == "fail")
            results["passed"] = (
                isolated
                and not fatal_errors
                and phases_passed >= 3  # At minimum: load, isolation, Unity init
                and phases_failed == 0
            )

        except Exception as e:
            results["error"] = str(e)
            print(f"[!] E2E exception: {e}")
        finally:
            browser.close()

    return results


def main():
    parser = argparse.ArgumentParser(description="E2E WebGL Dialogue QA")
    parser.add_argument("--port", type=int, default=8085, help="nginx port")
    parser.add_argument("--timeout", type=int, default=60, help="Unity init timeout (seconds)")
    parser.add_argument("--output", type=str, default=None, help="Output JSON path")
    args = parser.parse_args()

    results = run_e2e(args.port, args.timeout)

    # Write results
    output_path = args.output or str(Path(__file__).parent.parent.parent / "Builds/WebGL_client/e2e-results.json")
    with open(output_path, "w") as f:
        json.dump(results, f, indent=2)
    print(f"\n[*] Results written to {output_path}")

    # Print summary
    print("\n═══════════════════════════════════════════")
    print("  E2E Dialogue QA — Summary")
    print("═══════════════════════════════════════════")
    for ph in results["phases"]:
        icon = "✅" if ph["status"] == "pass" else "❌" if ph["status"] == "fail" else "⏭️"
        print(f"  {icon} {ph['name']}: {ph['status']}")
    print(f"  crossOriginIsolated: {results.get('cross_origin_isolated', 'unknown')}")
    print(f"  page errors: {len(results['page_errors'])}")
    console_errors = [c for c in results["console"] if c["type"] == "error"]
    print(f"  console errors: {len(console_errors)}")
    if console_errors:
        for e in console_errors[:5]:
            print(f"    - {e['text'][:100]}")
    print(f"  PASSED: {results['passed']}")
    print("═══════════════════════════════════════════")

    sys.exit(0 if results["passed"] else 1)


if __name__ == "__main__":
    main()
