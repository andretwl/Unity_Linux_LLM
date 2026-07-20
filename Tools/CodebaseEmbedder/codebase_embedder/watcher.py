from __future__ import annotations

from dataclasses import asdict
import json
import logging
from pathlib import Path
import threading
import time
from typing import Any

from watchdog.events import FileSystemEvent, FileSystemEventHandler
from watchdog.observers import Observer

from .asmdef_parser import AssemblyRecord, parse_asmdefs
from .chunking import chunk_docs, records_to_embedding_chunks
from .config import CodebaseEmbedderConfig
from .coverage import load_coverage_report
from .coverage_records import apply_coverage_to_records
from .csharp_analyzer import analyze_csharp_files
from .discovery import classify_unity_region, discover_project_files, is_excluded
from .embeddings import EmbeddingClient
from .indexer import (
    _build_namespace_summary_records,
    _build_runtime_summary_records,
    _write_report,
    load_chunks,
)
from .qdrant_store import QdrantStore
from .records import IndexRecord, RelationRecord, utc_now, write_json, write_jsonl
from .sparse import compute_sparse_vectors, SPARSE_VECTOR_NAME

logger = logging.getLogger("codebase-watcher")
logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(name)s: %(message)s")


def load_jsonl_records(path: Path) -> list[IndexRecord]:
    records = []
    if not path.exists():
        return records
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        obj = json.loads(line)
        records.append(
            IndexRecord(
                obj["record_type"],
                obj["stable_key"],
                obj["text"],
                obj.get("payload", {}),
                obj.get("point_id"),
            )
        )
    return records


def load_jsonl_relations(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def load_assemblies(path: Path) -> list[AssemblyRecord]:
    if not path.exists():
        return []
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        return [
            AssemblyRecord(
                name=asm["name"],
                path=asm["path"],
                root_namespace=asm.get("root_namespace", ""),
                references=asm.get("references", []),
                include_platforms=asm.get("include_platforms", []),
                exclude_platforms=asm.get("exclude_platforms", []),
                auto_referenced=asm.get("auto_referenced"),
                optional_unity_references=asm.get("optional_unity_references", []),
                raw=asm.get("raw", {}),
            )
            for asm in data
        ]
    except Exception as exc:
        logger.warning(f"Failed to load assemblies from {path}: {exc}")
        return []


class CodebaseWatcher:
    def __init__(self, config: CodebaseEmbedderConfig, debounce_seconds: float = 1.5):
        self.config = config
        self.debounce_seconds = debounce_seconds
        self.project_root = Path(config.project_root).resolve()
        self.observer = Observer()
        self.pending_changes: set[Path] = set()
        self.lock = threading.Lock()
        self.timer: threading.Timer | None = None
        self.running = False

    def start(self) -> None:
        if self.running:
            return
        self.running = True

        # Run a catch-up scan before accepting live events so we don't
        # miss changes made while the watcher was down.
        self._catch_up()

        handler = WatchdogHandler(self)
        self.observer.schedule(handler, str(self.project_root), recursive=True)
        self.observer.start()
        logger.info(f"Started monitoring workspace: {self.project_root}")

    def _catch_up(self) -> None:
        """Detect files changed while the watcher was stopped and queue them."""
        art = self.config.artifact_dir
        chunks_file = art / "chunks.jsonl"
        if not chunks_file.exists():
            logger.info("No local index found — skipping catch-up (full index will run on first change).")
            return

        # Collect paths we already have indexed
        indexed_paths: set[str] = set()
        for line in chunks_file.read_text(encoding="utf-8").splitlines():
            if not line.strip():
                continue
            try:
                obj = json.loads(line)
                path = obj.get("payload", {}).get("path")
                if path:
                    indexed_paths.add(path)
            except json.JSONDecodeError:
                continue

        # Scan current filesystem for relevant files
        from .discovery import discover_project_files
        files = discover_project_files(self.config)
        current_paths: set[str] = set()
        for path_list in (files.csharp, files.asmdefs, files.docs):
            for p in path_list:
                rel = p.relative_to(self.config.project_root).as_posix()
                current_paths.add(rel)

        # Detect new, modified, or deleted files
        changed: list[Path] = []
        root = Path(self.config.project_root)
        for rel in current_paths:
            if rel not in indexed_paths:
                changed.append(root / rel)
                logger.info(f"Catch-up: new file {rel}")

        # Also detect deleted files (indexed but no longer on disk)
        for rel in indexed_paths:
            if rel not in current_paths:
                p = root / rel
                if not p.exists():
                    changed.append(p)
                    logger.info(f"Catch-up: deleted file {rel}")

        if changed:
            logger.info(f"Catch-up: {len(changed)} files changed while watcher was down, queuing...")
            self._execute_incremental_update(changed)
        else:
            logger.info("Catch-up: no changes detected since last index.")

    def stop(self) -> None:
        if not self.running:
            return
        self.running = False
        with self.lock:
            if self.timer:
                self.timer.cancel()
        self.observer.stop()
        self.observer.join()
        logger.info("Stopped workspace monitoring.")

    def queue_change(self, path: Path) -> None:
        try:
            rel_path = path.relative_to(self.project_root)
        except ValueError:
            return

        if is_excluded(rel_path, self.config):
            return

        # We only care about .cs, .asmdef, and .md files (and .unity if scenes are included)
        suffix = path.suffix.lower()
        allowed_suffixes = {".cs", ".asmdef", ".md"}
        if self.config.include_scenes:
            allowed_suffixes.add(".unity")

        if suffix not in allowed_suffixes:
            return

        with self.lock:
            logger.info(f"Queued change for: {rel_path}")
            self.pending_changes.add(path)
            if self.timer:
                self.timer.cancel()
            self.timer = threading.Timer(self.debounce_seconds, self.process_changes)
            self.timer.start()

    def process_changes(self) -> None:
        with self.lock:
            changes = list(self.pending_changes)
            self.pending_changes.clear()
            self.timer = None

        if not changes:
            return

        logger.info(f"Processing {len(changes)} debounced file changes...")
        try:
            self._execute_incremental_update(changes)
        except Exception as exc:
            logger.error(f"Error executing incremental update: {exc}", exc_info=True)

    def _execute_incremental_update(self, changed_paths: list[Path]) -> None:
        art = self.config.artifact_dir
        chunks_file = art / "chunks.jsonl"
        symbols_file = art / "symbols.jsonl"
        relations_file = art / "relations.jsonl"

        if not chunks_file.exists():
            logger.warning("Local index database (chunks.jsonl) not found. Please run a full index command first.")
            return

        # Load existing local collections
        chunks = load_chunks(art)
        symbols = load_jsonl_records(symbols_file)
        relations_data = load_jsonl_relations(relations_file)

        # Reconstruct RelationRecords
        relations = [
            RelationRecord(
                r["relation_kind"],
                r["source"],
                r["target"],
                r.get("path", ""),
                {k: v for k, v in r.items() if k not in {"relation_kind", "source", "target", "path"}},
            )
            for r in relations_data
        ]

        deleted_point_ids: list[str] = []
        updated_paths_rel: set[str] = set()

        for path in changed_paths:
            try:
                rel = path.relative_to(self.project_root).as_posix()
            except ValueError:
                logger.warning(
                    f"Skipping path outside project root (stale artifact entry?): {path}"
                )
                continue
            updated_paths_rel.add(rel)

            # Find matching chunks to delete and collect their point IDs
            old_chunks = [c for c in chunks if c.payload.get("path") == rel]
            for c in old_chunks:
                if c.point_id:
                    deleted_point_ids.append(c.point_id)

            # Filter old records from local lists
            chunks = [c for c in chunks if c.payload.get("path") != rel]
            symbols = [s for s in symbols if s.payload.get("path") != rel]
            relations = [r for r in relations if r.path != rel]

        # Read assemblies and coverage report once for parsing C# files
        assemblies = load_assemblies(art / "asmdefs.json")
        coverage_report = load_coverage_report(self.project_root, self.config.coverage_report_dir_name)

        new_records: list[IndexRecord] = []
        new_relations: list[RelationRecord] = []

        # Parse new and modified files
        for path in changed_paths:
            if not path.exists():
                logger.info(f"File was deleted: {path}")
                continue

            rel = path.relative_to(self.project_root).as_posix()
            logger.info(f"Parsing updated file: {rel}")

            try:
                if path.suffix == ".cs":
                    file_records, file_relations = analyze_csharp_files(
                        self.project_root, [path], assemblies, self.config.project_slug
                    )
                    new_records.extend(file_records)
                    new_relations.extend(file_relations)

                    if self.config.collection_profile == "hierarchy":
                        new_records.extend(_build_namespace_summary_records(file_records, self.config.project_slug))
                    if self.config.collection_profile == "runtime":
                        new_records.extend(_build_runtime_summary_records(file_records, self.config.project_slug))

                    if coverage_report is not None:
                        apply_coverage_to_records(new_records, coverage_report)

                elif path.suffix == ".asmdef":
                    logger.info("Assembly definition modified. Re-parsing all asmdef files to keep consistency...")
                    files = discover_project_files(self.config)
                    assemblies, asm_relations = parse_asmdefs(self.project_root, files.asmdefs)
                    write_json(art / "asmdefs.json", [asdict(asm) for asm in assemblies])

                    # Create new index records for assemblies
                    new_records.extend(asm.to_index_record(self.config.project_slug) for asm in assemblies)
                    new_relations.extend(asm_relations)

                elif path.suffix == ".md" or (self.config.include_scenes and path.suffix == ".unity"):
                    new_records.extend(chunk_docs(self.config.project_slug, self.project_root, [path]))

            except Exception as exc:
                logger.error(f"Failed to parse {rel}: {exc}")

        # Extract structural embedded relations
        embedded_relation_kinds = {
            "asmdef-references",
            "inherits",
            "implements",
            "namespace-contains-type",
            "namespace-uses-namespace",
        }
        new_records.extend(
            rel.to_index_record(self.config.project_slug)
            for rel in new_relations
            if rel.relation_kind in embedded_relation_kinds
        )

        # Convert to final chunks
        new_chunks = records_to_embedding_chunks(new_records)

        # Merge new records into local databases
        chunks.extend(new_chunks)
        symbols.extend(new_records)
        relations.extend(new_relations)

        # Re-calculate counts
        files = discover_project_files(self.config)
        counts = {
            "csharp_files": len(files.csharp),
            "asmdef_files": len(files.asmdefs),
            "doc_files": len(files.docs),
            "records": len(symbols),
            "relations": len(relations),
            "chunks": len(chunks),
        }
        if coverage_report is not None:
            counts["coverage_classes"] = len(coverage_report.classes)

        # Write updated local databases
        write_json(
            art / "manifest.json",
            {
                "project": self.config.project_slug,
                "collection": self.config.collection_name,
                "indexed_at": utc_now(),
                "counts": counts,
            },
        )
        write_jsonl(symbols_file, symbols)
        write_jsonl(relations_file, relations)
        write_jsonl(chunks_file, chunks)
        _write_report(art / "index-report.md", counts, symbols)

        logger.info(f"Updated local databases for: {', '.join(updated_paths_rel)}")

        # Sync changes with Qdrant Vector DB
        emb = EmbeddingClient(self.config.localai_base_url, self.config.embedding_model)
        dim = emb.dimension()

        # Re-embed new chunks using Cache
        # Lazy import of embed_records_with_cache to avoid circular deps
        from .cli import embed_records_with_cache

        vectors, cache_stats = embed_records_with_cache(
            new_chunks, emb, art, dim, batch_size=32, use_cache=True
        )

        # Compute sparse vectors for code-keyword matching
        sparse_vectors = compute_sparse_vectors([c.text for c in new_chunks])

        store = QdrantStore(self.config.qdrant_url, self.config.collection_name)
        store.ensure_collection(dim)

        # 1. Delete old points from Qdrant
        if deleted_point_ids:
            logger.info(f"Deleting {len(deleted_point_ids)} outdated points from Qdrant...")
            store.delete(deleted_point_ids)

        # 2. Upsert new points to Qdrant (dense + sparse named vectors)
        if new_chunks:
            logger.info(f"Upserting {len(new_chunks)} updated points into Qdrant ({self.config.collection_name})...")
            store.upsert(new_chunks, vectors, sparse_vectors)

        logger.info("Incremental update and Qdrant synchronization complete.")


class WatchdogHandler(FileSystemEventHandler):
    def __init__(self, watcher: CodebaseWatcher):
        self.watcher = watcher

    def on_modified(self, event: FileSystemEvent) -> None:
        if not event.is_directory:
            self.watcher.queue_change(Path(event.src_path))

    def on_created(self, event: FileSystemEvent) -> None:
        if not event.is_directory:
            self.watcher.queue_change(Path(event.src_path))

    def on_deleted(self, event: FileSystemEvent) -> None:
        if not event.is_directory:
            self.watcher.queue_change(Path(event.src_path))

    def on_moved(self, event: FileSystemEvent) -> None:
        if not event.is_directory:
            self.watcher.queue_change(Path(event.src_path))
            self.watcher.queue_change(Path(event.dest_path))
