#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import time
import urllib.request
from pathlib import Path
from typing import Any


def request_json(method: str, url: str, body: dict[str, Any] | None = None, timeout: float = 30.0) -> tuple[int, dict[str, Any]]:
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        raw = resp.read().decode("utf-8")
        return resp.status, json.loads(raw) if raw else {}


def model_ids(models_response: dict[str, Any]) -> set[str]:
    return {str(item.get("id")) for item in models_response.get("data", []) if item.get("id")}


def gpu_memory() -> str | None:
    if not shutil.which("nvidia-smi"):
        return None
    try:
        return subprocess.check_output(
            ["nvidia-smi", "--query-gpu=memory.used,memory.total", "--format=csv,noheader"],
            text=True,
            timeout=5,
        ).strip()
    except Exception:  # noqa: BLE001
        return None


def timed_record(kind: str, model: str, fn) -> dict[str, Any]:
    before_gpu = gpu_memory()
    started = time.perf_counter()
    record: dict[str, Any] = {"kind": kind, "model": model, "ok": False}
    try:
        status, data = fn()
        record.update({"ok": True, "status": status, "response_keys": sorted(data.keys())})
        if kind == "embedding":
            vectors = data.get("data", [])
            record["vector_dimension"] = len(vectors[0].get("embedding", [])) if vectors else 0
        elif kind == "chat":
            content = ""
            choices = data.get("choices", [])
            if choices:
                content = str(choices[0].get("message", {}).get("content", ""))
            record["response_chars"] = len(content)
    except Exception as exc:  # noqa: BLE001
        record["error"] = repr(exc)
    record["seconds"] = round(time.perf_counter() - started, 6)
    record["gpu_memory_before"] = before_gpu
    record["gpu_memory_after"] = gpu_memory()
    return record


def run_benchmark(base_url: str, embedding_models: list[str], chat_models: list[str], timeout: float) -> list[dict[str, Any]]:
    records: list[dict[str, Any]] = []
    status, models = request_json("GET", f"{base_url.rstrip('/')}/models", timeout=timeout)
    available = model_ids(models)
    records.append({"kind": "models", "ok": True, "status": status, "model_count": len(available), "models": sorted(available)})

    for model in embedding_models:
        if model not in available:
            records.append({"kind": "embedding", "model": model, "ok": False, "error": "model not available"})
            continue
        records.append(timed_record(
            "embedding",
            model,
            lambda model=model: request_json("POST", f"{base_url.rstrip('/')}/embeddings", {"model": model, "input": "dimension probe"}, timeout=timeout),
        ))

    for model in chat_models:
        if model not in available:
            records.append({"kind": "chat", "model": model, "ok": False, "error": "model not available"})
            continue
        records.append(timed_record(
            "chat",
            model,
            lambda model=model: request_json(
                "POST",
                f"{base_url.rstrip('/')}/chat/completions",
                {"model": model, "messages": [{"role": "user", "content": "Reply with OK."}], "max_tokens": 8},
                timeout=timeout,
            ),
        ))
    return records


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Smoke-test LocalAI backend routes used by CodebaseEmbedder.")
    parser.add_argument("--base-url", default="http://127.0.0.1:8080/v1")
    parser.add_argument("--embedding-model", action="append", default=None)
    parser.add_argument("--chat-model", action="append", default=None)
    parser.add_argument("--skip-chat", action="store_true")
    parser.add_argument("--skip-embeddings", action="store_true")
    parser.add_argument("--timeout", type=float, default=60.0)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args(argv)

    embedding_models = [] if args.skip_embeddings else (args.embedding_model or ["nomic-embed-text-v1.5", "embeddinggemma-300m"])
    chat_models = [] if args.skip_chat else (args.chat_model or ["llama-3.1-8b-q4-k-m", "llama-3.1-8b-q4-k-m-vulkan", "qwen2.5-1.5b-instruct-q4-k-m"])
    records = run_benchmark(args.base_url, embedding_models, chat_models, args.timeout)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", encoding="utf-8") as handle:
        for record in records:
            handle.write(json.dumps(record, sort_keys=True) + "\n")
            print(json.dumps(record, sort_keys=True))
    return 0 if any(record.get("ok") and record.get("kind") == "embedding" for record in records) else 1


if __name__ == "__main__":
    raise SystemExit(main())
