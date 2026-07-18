from __future__ import annotations

import argparse
from contextlib import contextmanager
import json
from pathlib import Path
import socket
import sys
from time import perf_counter

from .audit import audit_project, format_audit_report
from .config import CodebaseEmbedderConfig
from .embeddings import EmbeddingClient
from .indexer import build_index, load_chunks
from .qdrant_store import QdrantStore
from .query import build_query_response, format_query_workflow, format_results, lexical_query, qdrant_query
from .records import IndexRecord
from .rules_engine import run_check
from .sparse import compute_sparse_vectors
from .vector_cache import VectorCache
from .advise import advise_asmdef, advise_placement, format_asmdef_advice, format_placement_advice


@contextmanager
def timed_stage(name: str, timings: dict[str, float]):
    start = perf_counter()
    try:
        yield
    finally:
        timings[name] = timings.get(name, 0.0) + (perf_counter() - start)


def write_timing_report(
    path: str | Path | None,
    *,
    command: str,
    config: CodebaseEmbedderConfig,
    timings: dict[str, float],
    counts: dict[str, int] | None = None,
    extra: dict[str, object] | None = None,
) -> None:
    if not path:
        return
    report = {
        "command": command,
        "collection": config.collection_name,
        "profile": config.collection_profile,
        "project": config.project_slug,
        "embedding_model": config.embedding_model,
        "qdrant_url": config.qdrant_url,
        "localai_base_url": config.localai_base_url,
        "counts": counts or {},
        "timings_seconds": {key: round(value, 6) for key, value in timings.items()},
    }
    if extra:
        report.update(extra)
    out = Path(path)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def embed_records_with_cache(
    records: list[IndexRecord],
    embedding_client: EmbeddingClient,
    artifact_dir: Path,
    dimension: int,
    *,
    batch_size: int,
    use_cache: bool,
) -> tuple[list[list[float]], dict[str, int]]:
    stats = {"cache_hits": 0, "cache_misses": 0, "embedded_records": 0}
    if not use_cache:
        vectors: list[list[float]] = []
        for start in range(0, len(records), batch_size):
            batch = records[start:start + batch_size]
            vectors.extend(embedding_client.embed([r.text for r in batch]))
            stats["embedded_records"] += len(batch)
            stats["cache_misses"] += len(batch)
        return vectors, stats

    cache = VectorCache(artifact_dir, embedding_client.model, dimension)
    vectors_by_index: dict[int, list[float]] = {}
    misses: list[tuple[int, IndexRecord]] = []
    for index, record in enumerate(records):
        cached = cache.get(record)
        if cached is None:
            misses.append((index, record))
            stats["cache_misses"] += 1
        else:
            vectors_by_index[index] = cached
            stats["cache_hits"] += 1

    for start in range(0, len(misses), batch_size):
        batch = misses[start:start + batch_size]
        batch_records = [record for _, record in batch]
        batch_vectors = embedding_client.embed([record.text for record in batch_records])
        stats["embedded_records"] += len(batch_records)
        for (index, record), vector in zip(batch, batch_vectors, strict=True):
            cache.put(record, vector)
            vectors_by_index[index] = vector

    return [vectors_by_index[index] for index in range(len(records))], stats


def _config(args: argparse.Namespace) -> CodebaseEmbedderConfig:
    return CodebaseEmbedderConfig(
        project_root=args.root,
        qdrant_url=args.qdrant_url,
        collection_name=args.collection,
        localai_base_url=args.localai_url,
        embedding_model=args.embedding_model,
        collection_profile=args.profile,
        include_scenes=getattr(args, "include_scenes", False),
        include_samples=getattr(args, "include_samples", False),
    )


def cmd_status(args: argparse.Namespace) -> int:
    cfg = _config(args)
    timings: dict[str, float] = {}
    with timed_stage("build_index", timings):
        result = build_index(cfg, write_artifacts=False)
    with timed_stage("qdrant_health", timings):
        qdrant = QdrantStore(cfg.qdrant_url, cfg.collection_name).health()
    print(f"Project: {cfg.project_slug}")
    print(f"C# files: {result.counts['csharp_files']}")
    print(f"asmdef files: {result.counts['asmdef_files']}")
    print(f"doc files: {result.counts['doc_files']}")
    print(f"Qdrant: {'reachable' if qdrant else 'unreachable'}")
    print(f"Collection: {cfg.collection_name}")
    write_timing_report(args.timings_json, command="status", config=cfg, timings=timings, counts=result.counts, extra={"qdrant_reachable": qdrant})
    return 0


def cmd_scan(args: argparse.Namespace) -> int:
    cfg = _config(args)
    timings: dict[str, float] = {}
    with timed_stage("build_index", timings):
        result = build_index(cfg, write_artifacts=True)
    print(json.dumps(result.counts, indent=2, sort_keys=True))
    print(f"Artifacts: {result.artifact_dir}")
    write_timing_report(args.timings_json, command="scan", config=cfg, timings=timings, counts=result.counts)
    return 0


def cmd_index(args: argparse.Namespace) -> int:
    cfg = _config(args)
    timings: dict[str, float] = {}
    result = None
    if args.reuse_artifacts:
        records = load_chunks(cfg.artifact_dir)
        if not records and args.fail_if_artifacts_missing:
            raise SystemExit(f"No existing chunks found at {cfg.artifact_dir / 'chunks.jsonl'}")
        counts = {"records": len(records), "chunks": len(records)}
    else:
        with timed_stage("build_index", timings):
            result = build_index(cfg, write_artifacts=True)
        counts = result.counts
        records = load_chunks(cfg.artifact_dir)
    if args.no_qdrant:
        print(json.dumps(counts, indent=2, sort_keys=True))
        print("Qdrant upsert skipped (--no-qdrant).")
        write_timing_report(args.timings_json, command="index", config=cfg, timings=timings, counts=counts, extra={"qdrant_skipped": True, "reuse_artifacts": args.reuse_artifacts})
        return 0
    with timed_stage("embedding_dimension", timings):
        emb = EmbeddingClient(cfg.localai_base_url, cfg.embedding_model)
        dim = emb.dimension()
    with timed_stage("embedding", timings):
        vectors, cache_stats = embed_records_with_cache(records, emb, cfg.artifact_dir, dim, batch_size=args.batch_size, use_cache=args.use_vector_cache)
    with timed_stage("sparse_vectors", timings):
        sparse_vectors = compute_sparse_vectors([r.text for r in records])
    with timed_stage("qdrant_collection", timings):
        store = QdrantStore(cfg.qdrant_url, cfg.collection_name)
        if args.clear:
            existed = store.clear()
            print(f"Cleared Qdrant collection '{cfg.collection_name}' (existed={existed}).")
        store.ensure_collection(dim)
    upserted_records = 0
    for start in range(0, len(records), args.batch_size):
        batch = records[start:start + args.batch_size]
        batch_vectors = vectors[start:start + args.batch_size]
        batch_sparse = sparse_vectors[start:start + args.batch_size]
        with timed_stage("qdrant_upsert", timings):
            store.upsert(batch, batch_vectors, batch_sparse)
        upserted_records += len(batch)
        print(f"Upserted {min(start + len(batch), len(records))}/{len(records)} records", flush=True)
    print(f"Upserted {len(records)} records into {cfg.collection_name}")
    write_timing_report(
        args.timings_json,
        command="index",
        config=cfg,
        timings=timings,
        counts=counts,
        extra={**cache_stats, "upserted_records": upserted_records, "batch_size": args.batch_size, "use_vector_cache": args.use_vector_cache, "reuse_artifacts": args.reuse_artifacts},
    )
    return 0


def cmd_query(args: argparse.Namespace) -> int:
    cfg = _config(args)
    timings: dict[str, float] = {}
    try:
        with timed_stage("query", timings):
            response = build_query_response(cfg, args.question, args.limit, local=args.local)
    except Exception as exc:  # noqa: BLE001
        if args.local:
            raise
        print(f"Qdrant query failed ({exc}); falling back to local lexical artifacts.", file=sys.stderr)
        with timed_stage("query_fallback_local", timings):
            response = build_query_response(cfg, args.question, args.limit, local=True)
    results = response["results"]
    if args.json:
        print(json.dumps(response, indent=2, sort_keys=True))
    else:
        print(format_query_workflow(args.question))
        print("")
        print(format_results(results))
    write_timing_report(args.timings_json, command="query", config=cfg, timings=timings, extra={"question": args.question, "result_count": len(results), "local": args.local})
    return 0


def cmd_unity_validate(args: argparse.Namespace) -> int:
    host, port = "localhost", 8765
    reachable = False
    try:
        with socket.create_connection((host, port), timeout=2):
            reachable = True
    except OSError:
        reachable = False
    print(json.dumps({
        "bridge_host": host,
        "bridge_port": port,
        "tcp_reachable": reachable,
        "note": "GladeKit MCP resources are accessed through Hermes MCP tools; this CLI validates bridge reachability only.",
    }, indent=2))
    return 0 if reachable else 2


def cmd_audit(args: argparse.Namespace) -> int:
    cfg = _config(args)
    timings: dict[str, float] = {}
    with timed_stage("audit", timings):
        report = audit_project(
            cfg,
            script_path=args.script,
            prompts=args.prompt or None,
            scenario=args.scenario,
            use_qdrant=not args.local,
            limit=args.limit,
            scene_path=args.scene,
        )
    if args.json:
        print(json.dumps(report, indent=2, sort_keys=True))
    else:
        print(format_audit_report(report))
    write_timing_report(args.timings_json, command="audit", config=cfg, timings=timings, extra={"scenario": args.scenario, "local": args.local})
    return 0


def cmd_watch(args: argparse.Namespace) -> int:
    import time
    from .watcher import CodebaseWatcher
    from .indexer import build_index

    cfg = _config(args)
    art = cfg.artifact_dir
    if not (art / "chunks.jsonl").exists():
        print("Local index database not found. Initiating full index scan first...", flush=True)
        build_index(cfg, write_artifacts=True)
        print("Full index scan completed.", flush=True)

    watcher = CodebaseWatcher(cfg, debounce_seconds=args.debounce)
    watcher.start()

    print("Press Ctrl+C to exit.", flush=True)
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nStopping watcher...", flush=True)
    finally:
        watcher.stop()
    return 0


def cmd_check(args: argparse.Namespace) -> int:
    """Run config-driven codebase rule checks."""
    root = Path(args.root).resolve()
    rules_path = root / ".codebaserules.yaml"
    return run_check(
        project_root=root,
        rules_path=rules_path if args.rules is None else Path(args.rules),
        target_dir=args.target,
        output_path=args.output,
        min_severity=args.min_severity,
        json_output=args.json,
        exclude_patterns=args.exclude or None,
    )


def cmd_advise(args: argparse.Namespace) -> int:
    """Deterministic placement / asmdef advice over structure-graph.json."""
    cfg = _config(args)
    art = cfg.artifact_dir
    if args.advise_command == "placement":
        advice = advise_placement(art, args.feature, limit=args.limit)
        if args.json:
            print(json.dumps(advice, indent=2, sort_keys=True))
        else:
            print(format_placement_advice(advice))
        return 0
    if args.advise_command == "asmdef":
        usings = [u.strip() for u in (args.usings or "").split(",") if u.strip()]
        advice = advise_asmdef(
            art,
            name=args.name,
            folder=args.folder,
            usings=usings,
            root_namespace=args.root_namespace,
        )
        if args.json:
            print(json.dumps(advice, indent=2, sort_keys=True))
        else:
            print(format_asmdef_advice(advice))
        return 0
    raise SystemExit(f"Unknown advise command: {args.advise_command}")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="codebase-embedder")
    add_common_args(parser)
    sub = parser.add_subparsers(dest="command", required=True)
    for name, func in [("status", cmd_status), ("scan", cmd_scan)]:
        p = sub.add_parser(name)
        add_common_args(p)
        p.add_argument("--include-samples", action="store_true")
        p.set_defaults(func=func)
    p = sub.add_parser("index")
    add_common_args(p)
    p.add_argument("--include-samples", action="store_true")
    p.add_argument("--no-qdrant", action="store_true")
    p.add_argument("--batch-size", type=int, default=32)
    p.add_argument("--use-vector-cache", action="store_true", help="Cache embeddings by model, vector dimension, point id, and text hash")
    p.add_argument("--reuse-artifacts", action="store_true", help="Load existing .codebase-index/chunks.jsonl instead of rebuilding artifacts")
    p.add_argument("--fail-if-artifacts-missing", action="store_true", help="With --reuse-artifacts, fail if chunks.jsonl is missing or empty")
    p.add_argument("--clear", action="store_true", help="Delete all points from the Qdrant collection before indexing (clean re-index)")
    p.set_defaults(func=cmd_index)
    p = sub.add_parser("query")
    add_common_args(p)
    p.add_argument("question")
    p.add_argument("--limit", type=int, default=8)
    p.add_argument("--json", action="store_true")
    p.add_argument("--local", action="store_true", help="Use local lexical artifact search instead of Qdrant")
    p.set_defaults(func=cmd_query)
    p = sub.add_parser("unity-validate")
    add_common_args(p)
    p.set_defaults(func=cmd_unity_validate)
    p = sub.add_parser("audit")
    add_common_args(p)
    p.add_argument("--script", help="Asset-relative script path to audit deeply")
    p.add_argument("--scene", help="Asset-relative Unity scene path for scene-aware audit overlay")
    p.add_argument("--scenario", choices=["localai-llmunity"], help="Run a preset retrieval audit")
    p.add_argument("--prompt", action="append", help="Add one smoke-query prompt (repeatable)")
    p.add_argument("--limit", type=int, default=5)
    p.add_argument("--json", action="store_true")
    p.add_argument("--local", action="store_true", help="Use local artifacts instead of live Qdrant")
    p.set_defaults(func=cmd_audit)
    p = sub.add_parser("watch")
    add_common_args(p)
    p.add_argument("--debounce", type=float, default=1.5, help="Quiet period in seconds to debounce file events")
    p.set_defaults(func=cmd_watch)
    p = sub.add_parser("check")
    add_common_args(p)
    p.add_argument("--target", help="Target subdirectory (e.g., Assets/Scripts/Runtime)")
    p.add_argument("--output", help="Write report to file")
    p.add_argument("--rules", help="Path to .codebaserules.yaml (default: project root)")
    p.add_argument("--exclude", action="append", default=[],
                    help="Glob pattern to exclude (repeatable, e.g. --exclude 'Packages/**' --exclude 'Assets/Editor/**')")
    p.add_argument("--min-severity", default="Suggestion", choices=["Error", "Warning", "Suggestion", "Info"],
                    help="Minimum severity to report (default: Suggestion)")
    p.add_argument("--json", action="store_true", help="Output as JSON")
    p.set_defaults(func=cmd_check)

    advise = sub.add_parser("advise", help="Deterministic placement / asmdef advice from structure-graph.json")
    add_common_args(advise)
    advise_sub = advise.add_subparsers(dest="advise_command", required=True)
    place = advise_sub.add_parser("placement", help="Suggest namespace/folder/asmdef for a feature")
    add_common_args(place)
    place.add_argument("--feature", required=True, help="Feature description, e.g. 'dialogue history persistence'")
    place.add_argument("--limit", type=int, default=5)
    place.add_argument("--json", action="store_true")
    place.set_defaults(func=cmd_advise)
    asm = advise_sub.add_parser("asmdef", help="Predict references for a new asmdef")
    add_common_args(asm)
    asm.add_argument("--name", required=True, help="New assembly name, e.g. NPCSystem.Presence")
    asm.add_argument("--folder", required=True, help="Target folder, e.g. Assets/Scripts/Runtime/Presence")
    asm.add_argument("--usings", default="", help="Comma-separated namespaces the new code will import")
    asm.add_argument("--root-namespace", default=None, help="Optional rootNamespace override")
    asm.add_argument("--json", action="store_true")
    asm.set_defaults(func=cmd_advise)
    return parser


def add_common_args(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--root", default="../..")
    parser.add_argument("--qdrant-url", default="http://localhost:6333")
    parser.add_argument("--collection", default="unity_linux_llm_codebase_v2")
    parser.add_argument("--profile", default="runtime", choices=["baseline", "hierarchy", "runtime"])
    parser.add_argument("--include-scenes", action="store_true")
    parser.add_argument("--localai-url", default="http://localhost:8080/v1")
    parser.add_argument("--embedding-model", default="nomic-embed-text-v1.5")
    parser.add_argument("--timings-json", help="Write command stage timings to this JSON file")


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
