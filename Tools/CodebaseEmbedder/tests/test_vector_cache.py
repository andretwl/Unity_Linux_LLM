from __future__ import annotations

from pathlib import Path

from codebase_embedder.records import IndexRecord
from codebase_embedder.vector_cache import VectorCache


def test_vector_cache_reuses_only_same_model_dimension_and_text(tmp_path: Path):
    cache = VectorCache(tmp_path, model="nomic-embed-text-v1.5", dimension=3)
    record = IndexRecord("type", "type:NPCSystem.Foo:Assets/Foo.cs", "old text", {"path": "Assets/Foo.cs"})

    cache.put(record, [1.0, 2.0, 3.0])

    assert cache.get(record) == [1.0, 2.0, 3.0]
    assert cache.get(IndexRecord("type", record.stable_key, "new text", record.payload)) is None
    assert VectorCache(tmp_path, model="other", dimension=3).get(record) is None
    assert VectorCache(tmp_path, model="nomic-embed-text-v1.5", dimension=4).get(record) is None


def test_vector_cache_rejects_wrong_dimension(tmp_path: Path):
    cache = VectorCache(tmp_path, model="nomic-embed-text-v1.5", dimension=3)
    record = IndexRecord("type", "type:NPCSystem.Foo:Assets/Foo.cs", "text", {})

    try:
        cache.put(record, [1.0, 2.0])
    except ValueError as exc:
        assert "dimension" in str(exc)
    else:
        raise AssertionError("expected wrong-dimension vectors to fail")
