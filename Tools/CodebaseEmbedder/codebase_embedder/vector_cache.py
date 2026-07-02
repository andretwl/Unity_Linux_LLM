from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Any

from .records import IndexRecord, utc_now


def _safe_name(value: str) -> str:
    return "".join(ch if ch.isalnum() or ch in {"-", "_", "."} else "_" for ch in value)


class VectorCache:
    def __init__(self, artifact_dir: Path | str, model: str, dimension: int):
        self.artifact_dir = Path(artifact_dir)
        self.model = model
        self.dimension = dimension
        self.path = self.artifact_dir / "vector-cache" / _safe_name(model) / f"{dimension}.jsonl"
        self._entries: dict[str, list[float]] | None = None

    def key_for(self, record: IndexRecord) -> str:
        text_hash = hashlib.sha256(record.text.encode("utf-8")).hexdigest()
        return f"{self.model}:{self.dimension}:{record.point_id}:{text_hash}"

    def get(self, record: IndexRecord) -> list[float] | None:
        entries = self._load()
        vector = entries.get(self.key_for(record))
        return list(vector) if vector is not None else None

    def put(self, record: IndexRecord, vector: list[float]) -> None:
        if len(vector) != self.dimension:
            raise ValueError(f"vector dimension {len(vector)} does not match cache dimension {self.dimension}")
        entries = self._load()
        key = self.key_for(record)
        entries[key] = list(vector)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "key": key,
            "model": self.model,
            "dimension": self.dimension,
            "record_type": record.record_type,
            "point_id": record.point_id,
            "stable_key": record.stable_key,
            "path": record.payload.get("path", ""),
            "created_at": utc_now(),
            "vector": vector,
        }
        with self.path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(payload, sort_keys=True) + "\n")

    def _load(self) -> dict[str, list[float]]:
        if self._entries is not None:
            return self._entries
        entries: dict[str, list[float]] = {}
        if self.path.exists():
            with self.path.open(encoding="utf-8") as handle:
                for line in handle:
                    if not line.strip():
                        continue
                    try:
                        obj: dict[str, Any] = json.loads(line)
                    except json.JSONDecodeError:
                        continue
                    if obj.get("model") != self.model or obj.get("dimension") != self.dimension:
                        continue
                    key = str(obj.get("key") or "")
                    vector = obj.get("vector")
                    if key and isinstance(vector, list) and len(vector) == self.dimension:
                        entries[key] = [float(value) for value in vector]
        self._entries = entries
        return entries
