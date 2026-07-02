#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import statistics
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from codebase_embedder.embeddings import EmbeddingClient  # noqa: E402


def parse_batch_sizes(value: str) -> list[int]:
    sizes = [int(part.strip()) for part in value.split(",") if part.strip()]
    if not sizes:
        raise ValueError("at least one batch size is required")
    if any(size <= 0 for size in sizes):
        raise ValueError("batch sizes must be positive")
    return sizes


def percentile(values: list[float], pct: int) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    if pct <= 0:
        return ordered[0]
    if pct >= 100:
        return ordered[-1]
    index = round((len(ordered) - 1) * (pct / 100))
    return ordered[index]


def load_chunk_texts(path: Path, limit: int | None = None) -> list[str]:
    texts: list[str] = []
    with path.open(encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            obj = json.loads(line)
            texts.append(str(obj.get("text") or ""))
            if limit and len(texts) >= limit:
                break
    return [text for text in texts if text]


def benchmark_model(base_url: str, model: str, texts: list[str], batch_sizes: list[int], timeout: float) -> dict:
    client = EmbeddingClient(base_url, model, timeout=timeout)
    results = []
    for batch_size in batch_sizes:
        latencies: list[float] = []
        records = 0
        errors = 0
        vector_dimension = 0
        started = time.perf_counter()
        for start in range(0, len(texts), batch_size):
            batch = texts[start:start + batch_size]
            request_started = time.perf_counter()
            try:
                vectors = client.embed(batch)
                latencies.append(time.perf_counter() - request_started)
                records += len(batch)
                if vectors and vectors[0]:
                    vector_dimension = len(vectors[0])
            except Exception as exc:  # noqa: BLE001
                errors += 1
                latencies.append(time.perf_counter() - request_started)
                print(f"embedding batch failed model={model} batch_size={batch_size}: {exc}", file=sys.stderr)
        elapsed = time.perf_counter() - started
        results.append({
            "model": model,
            "batch_size": batch_size,
            "records": records,
            "seconds": round(elapsed, 6),
            "records_per_second": round(records / elapsed, 6) if elapsed else 0.0,
            "request_count": len(latencies),
            "p50_latency_seconds": round(percentile(latencies, 50), 6),
            "p95_latency_seconds": round(percentile(latencies, 95), 6),
            "error_count": errors,
            "vector_dimension": vector_dimension,
        })
    return {"base_url": base_url, "model": model, "input_count": len(texts), "results": results}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Benchmark LocalAI embedding throughput over codebase chunks.")
    parser.add_argument("--chunks", required=True, type=Path)
    parser.add_argument("--model", default="nomic-embed-text-v1.5")
    parser.add_argument("--base-url", default="http://127.0.0.1:8080/v1")
    parser.add_argument("--batch-sizes", default="1,4,8,16,32,64")
    parser.add_argument("--limit", type=int, default=256, help="Maximum chunks to benchmark; 0 means all chunks")
    parser.add_argument("--timeout", type=float, default=60.0)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args(argv)

    texts = load_chunk_texts(args.chunks, None if args.limit == 0 else args.limit)
    report = benchmark_model(args.base_url, args.model, texts, parse_batch_sizes(args.batch_sizes), args.timeout)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
