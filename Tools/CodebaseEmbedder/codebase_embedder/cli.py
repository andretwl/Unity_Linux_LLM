from __future__ import annotations

import argparse
import json
import socket
import sys

from .audit import audit_project, format_audit_report
from .config import CodebaseEmbedderConfig
from .embeddings import EmbeddingClient
from .indexer import build_index, load_chunks
from .qdrant_store import QdrantStore
from .query import build_query_response, format_query_workflow, format_results, lexical_query, qdrant_query


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
    result = build_index(cfg, write_artifacts=False)
    qdrant = QdrantStore(cfg.qdrant_url, cfg.collection_name).health()
    print(f"Project: {cfg.project_slug}")
    print(f"C# files: {result.counts['csharp_files']}")
    print(f"asmdef files: {result.counts['asmdef_files']}")
    print(f"doc files: {result.counts['doc_files']}")
    print(f"Qdrant: {'reachable' if qdrant else 'unreachable'}")
    print(f"Collection: {cfg.collection_name}")
    return 0


def cmd_scan(args: argparse.Namespace) -> int:
    cfg = _config(args)
    result = build_index(cfg, write_artifacts=True)
    print(json.dumps(result.counts, indent=2, sort_keys=True))
    print(f"Artifacts: {result.artifact_dir}")
    return 0


def cmd_index(args: argparse.Namespace) -> int:
    cfg = _config(args)
    result = build_index(cfg, write_artifacts=True)
    if args.no_qdrant:
        print(json.dumps(result.counts, indent=2, sort_keys=True))
        print("Qdrant upsert skipped (--no-qdrant).")
        return 0
    records = load_chunks(cfg.artifact_dir)
    emb = EmbeddingClient(cfg.localai_base_url, cfg.embedding_model)
    dim = emb.dimension()
    store = QdrantStore(cfg.qdrant_url, cfg.collection_name)
    store.ensure_collection(dim)
    for start in range(0, len(records), args.batch_size):
        batch = records[start:start + args.batch_size]
        vectors = emb.embed([r.text for r in batch])
        store.upsert(batch, vectors)
        print(f"Upserted {min(start + len(batch), len(records))}/{len(records)} records", flush=True)
    print(f"Upserted {len(records)} records into {cfg.collection_name}")
    return 0


def cmd_query(args: argparse.Namespace) -> int:
    cfg = _config(args)
    try:
        response = build_query_response(cfg, args.question, args.limit, local=args.local)
    except Exception as exc:  # noqa: BLE001
        if args.local:
            raise
        print(f"Qdrant query failed ({exc}); falling back to local lexical artifacts.", file=sys.stderr)
        response = build_query_response(cfg, args.question, args.limit, local=True)
    results = response["results"]
    if args.json:
        print(json.dumps(response, indent=2, sort_keys=True))
    else:
        print(format_query_workflow(args.question))
        print("")
        print(format_results(results))
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
    return 0


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
    return parser


def add_common_args(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--root", default="../..")
    parser.add_argument("--qdrant-url", default="http://localhost:6333")
    parser.add_argument("--collection", default="unity_linux_llm_codebase_v1")
    parser.add_argument("--profile", default="baseline", choices=["baseline", "hierarchy", "runtime"])
    parser.add_argument("--include-scenes", action="store_true")
    parser.add_argument("--localai-url", default="http://localhost:8080/v1")
    parser.add_argument("--embedding-model", default="nomic-embed-text-v1.5")


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
