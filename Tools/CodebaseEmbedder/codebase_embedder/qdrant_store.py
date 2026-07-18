from __future__ import annotations

import json
import urllib.error
import urllib.request
from dataclasses import dataclass
from typing import Any

from .records import IndexRecord


# Name of the sparse vector in Qdrant's named-vector schema
SPARSE_VECTOR_NAME = "code_keywords"


@dataclass(slots=True)
class QdrantStore:
    url: str
    collection: str
    timeout: float = 20.0

    @property
    def base(self) -> str:
        return self.url.rstrip("/")

    # ------------------------------------------------------------------
    # Low-level HTTP helpers
    # ------------------------------------------------------------------

    def _request(
        self, method: str, path: str, body: dict[str, Any] | None = None
    ) -> dict[str, Any]:
        data = json.dumps(body).encode("utf-8") if body is not None else None
        req = urllib.request.Request(
            self.base + path,
            data=data,
            method=method,
            headers={"Content-Type": "application/json"},
        )
        with urllib.request.urlopen(req, timeout=self.timeout) as resp:
            raw = resp.read().decode("utf-8")
            return json.loads(raw) if raw else {}

    # ------------------------------------------------------------------
    # Health
    # ------------------------------------------------------------------

    def health(self) -> bool:
        try:
            self._request("GET", "/collections")
            return True
        except Exception:
            return False

    # ------------------------------------------------------------------
    # Collection lifecycle
    # ------------------------------------------------------------------

    def clear(self) -> bool:
        """Delete the entire collection so it can be recreated fresh.

        Returns True if the collection existed and was deleted, False if
        it did not exist.
        """
        try:
            self._request("DELETE", f"/collections/{self.collection}")
            return True
        except urllib.error.HTTPError as exc:
            if exc.code == 404:
                return False
            raise

    def ensure_collection(self, vector_size: int) -> None:
        """Create or verify the collection with named dense + sparse vectors.

        The collection stores:
        - ``dense`` — a dense embedding vector (Cosine distance), used for
          semantic similarity.
        - ``code_keywords`` — a sparse keyword vector (no distance), used for
          exact-token / BM25-style code symbol matching.
        """
        try:
            self._request("GET", f"/collections/{self.collection}")
            self.ensure_payload_indexes()
            return
        except urllib.error.HTTPError as exc:
            if exc.code != 404:
                raise

        self._request("PUT", f"/collections/{self.collection}", {
            "vectors": {
                "dense": {"size": vector_size, "distance": "Cosine", "on_disk": False},
            },
            "sparse_vectors": {
                SPARSE_VECTOR_NAME: {},
            },
            "on_disk_payload": True,
            "hnsw_config": {
                "m": 16,
                "ef_construct": 200,
                "full_scan_threshold": 10000,
                "on_disk": False,
            },
            "optimizers_config": {
                "default_segment_number": 2,
                "indexing_threshold": 10000,
            },
            "quantization_config": {
                "scalar": {"type": "int8", "always_ram": True},
            },
        })
        self.ensure_payload_indexes()

    def ensure_payload_indexes(self) -> None:
        keyword_fields = [
            "project", "record_type", "path", "unity_region", "asmdef", "namespace",
            "type_name", "member_name", "symbol_kind", "runtime_role",
            "relation_kind", "source", "target", "using_namespace",
            "coverage_bucket", "coverage_method_bucket", "coverage_class_name",
        ]
        integer_fields = ["line_start", "line_end", "chunk_index"]
        float_fields = [
            "coverage_line_rate", "coverage_method_rate",
            "coverage_file_line_rate", "coverage_file_method_rate",
            "coverage_project_line_rate", "coverage_project_method_rate",
            "coverage_method_line_rate", "coverage_method_crap_score",
        ]
        text_fields = ["heading"]
        for field in keyword_fields:
            self._create_payload_index(field, "keyword")
        for field in integer_fields:
            self._create_payload_index(field, "integer")
        for field in float_fields:
            self._create_payload_index(field, "float")
        for field in text_fields:
            self._create_payload_index(field, "text")

    def _create_payload_index(self, field_name: str, field_schema: str) -> None:
        try:
            self._request(
                "PUT",
                f"/collections/{self.collection}/index",
                {"field_name": field_name, "field_schema": field_schema},
            )
        except urllib.error.HTTPError as exc:
            if exc.code != 400:
                raise

    # ------------------------------------------------------------------
    # Write operations
    # ------------------------------------------------------------------

    def upsert(
        self,
        records: list[IndexRecord],
        dense_vectors: list[list[float]],
        sparse_vectors: list[dict[str, Any]] | None = None,
    ) -> None:
        """Upsert points with named dense + optional sparse vectors."""
        points = []
        for i, rec in enumerate(records):
            vec: dict[str, Any] = {"dense": dense_vectors[i]}
            if sparse_vectors is not None:
                vec[SPARSE_VECTOR_NAME] = sparse_vectors[i]
            points.append({
                "id": rec.point_id,
                "vector": vec,
                "payload": {**rec.payload, "text": rec.text},
            })
        for start in range(0, len(points), 64):
            self._request(
                "PUT",
                f"/collections/{self.collection}/points?wait=true",
                {"points": points[start : start + 64]},
            )

    def delete(self, point_ids: list[str]) -> None:
        if not point_ids:
            return
        self._request(
            "POST",
            f"/collections/{self.collection}/points/delete?wait=true",
            {"points": point_ids},
        )

    # ------------------------------------------------------------------
    # Search
    # ------------------------------------------------------------------

    def search(
        self,
        vector: list[float],
        limit: int = 8,
        query_filter: dict[str, Any] | None = None,
    ) -> list[dict[str, Any]]:
        """Dense-only search (backwards-compatible)."""
        body: dict[str, Any] = {
            "vector": {"name": "dense", "vector": vector},
            "limit": limit,
            "with_payload": True,
        }
        if query_filter:
            body["filter"] = query_filter
        data = self._request(
            "POST",
            f"/collections/{self.collection}/points/search",
            body,
        )
        return data.get("result", [])

    def search_hybrid(
        self,
        dense_vector: list[float],
        sparse_vector: dict[str, Any],
        limit: int = 8,
        prefetch_limit: int = 100,
        query_filter: dict[str, Any] | None = None,
    ) -> list[dict[str, Any]]:
        """Hybrid search using dense + sparse vectors with RRF fusion.

        Runs a dense and a sparse prefetch in parallel, then fuses them with
        Reciprocal Rank Fusion (Qdrant server-side).  Falls back to dense-only
        if the sparse vector is empty.
        """
        # Sparse vector is genuinely empty or a sentinel → dense-only fallback
        sparse_indices = sparse_vector.get("indices", [])
        if not sparse_indices or (len(sparse_indices) == 1 and sparse_indices[0] == 0):
            return self.search(dense_vector, limit=limit, query_filter=query_filter)

        prefetch: list[dict[str, Any]] = [
            {
                "query": dense_vector,
                "using": "dense",
                "limit": prefetch_limit,
            },
            {
                "query": sparse_vector,
                "using": SPARSE_VECTOR_NAME,
                "limit": prefetch_limit,
            },
        ]
        if query_filter:
            prefetch[0]["filter"] = query_filter
            prefetch[1]["filter"] = query_filter

        body: dict[str, Any] = {
            "vector": {"name": "dense", "vector": dense_vector},
            "prefetch": prefetch,
            "query": {"fusion": "rrf"},
            "limit": limit,
            "with_payload": True,
        }
        data = self._request(
            "POST",
            f"/collections/{self.collection}/points/search",
            body,
        )
        return data.get("result", [])
