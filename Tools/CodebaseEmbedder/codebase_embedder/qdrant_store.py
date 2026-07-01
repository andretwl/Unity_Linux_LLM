from __future__ import annotations

import json
import urllib.error
import urllib.request
from dataclasses import dataclass
from typing import Any

from .records import IndexRecord


@dataclass(slots=True)
class QdrantStore:
    url: str
    collection: str
    timeout: float = 20.0

    @property
    def base(self) -> str:
        return self.url.rstrip("/")

    def _request(self, method: str, path: str, body: dict[str, Any] | None = None) -> dict[str, Any]:
        data = json.dumps(body).encode("utf-8") if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, method=method, headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=self.timeout) as resp:
            raw = resp.read().decode("utf-8")
            return json.loads(raw) if raw else {}

    def health(self) -> bool:
        try:
            self._request("GET", "/collections")
            return True
        except Exception:
            return False

    def ensure_collection(self, vector_size: int) -> None:
        try:
            self._request("GET", f"/collections/{self.collection}")
            self.ensure_payload_indexes()
            return
        except urllib.error.HTTPError as exc:
            if exc.code != 404:
                raise
        self._request("PUT", f"/collections/{self.collection}", {
            "vectors": {"size": vector_size, "distance": "Cosine", "on_disk": False},
            "on_disk_payload": True,
            "hnsw_config": {"m": 16, "ef_construct": 100, "full_scan_threshold": 10000},
            "optimizers_config": {"default_segment_number": 2, "indexing_threshold": 1},
        })
        self.ensure_payload_indexes()

    def ensure_payload_indexes(self) -> None:
        keyword_fields = [
            "project", "record_type", "path", "unity_region", "asmdef", "namespace",
            "type_name", "member_name", "symbol_kind", "runtime_role", "relation_kind", "source", "target", "using_namespace",
        ]
        integer_fields = ["line_start", "line_end", "chunk_index"]
        text_fields = ["heading"]
        for field in keyword_fields:
            self._create_payload_index(field, "keyword")
        for field in integer_fields:
            self._create_payload_index(field, "integer")
        for field in text_fields:
            self._create_payload_index(field, "text")

    def _create_payload_index(self, field_name: str, field_schema: str) -> None:
        try:
            self._request("PUT", f"/collections/{self.collection}/index", {"field_name": field_name, "field_schema": field_schema})
        except urllib.error.HTTPError as exc:
            # Qdrant returns 400 when an index with the same params already exists.
            if exc.code != 400:
                raise

    def upsert(self, records: list[IndexRecord], vectors: list[list[float]]) -> None:
        points = []
        for rec, vec in zip(records, vectors, strict=True):
            points.append({"id": rec.point_id, "vector": vec, "payload": {**rec.payload, "text": rec.text}})
        for start in range(0, len(points), 64):
            self._request("PUT", f"/collections/{self.collection}/points?wait=true", {"points": points[start:start+64]})

    def search(self, vector: list[float], limit: int = 8, query_filter: dict[str, Any] | None = None) -> list[dict[str, Any]]:
        body: dict[str, Any] = {"vector": vector, "limit": limit, "with_payload": True}
        if query_filter:
            body["filter"] = query_filter
        data = self._request("POST", f"/collections/{self.collection}/points/search", body)
        return data.get("result", [])
