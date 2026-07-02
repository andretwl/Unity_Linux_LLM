import json
from pathlib import Path

from codebase_embedder.cli import embed_records_with_cache, write_timing_report
from codebase_embedder.config import CodebaseEmbedderConfig
from codebase_embedder.indexer import build_index
from codebase_embedder.query import build_query_response, format_query_workflow, lexical_query
from codebase_embedder.records import IndexRecord


def test_build_index_writes_artifacts(tmp_path: Path):
    (tmp_path / "Assets/Scripts/Runtime").mkdir(parents=True)
    (tmp_path / "Assets/Scripts/Runtime/NPCSystem.Runtime.asmdef").write_text(json.dumps({"name":"NPCSystem.Runtime","rootNamespace":"NPCSystem","references":[]}))
    (tmp_path / "Assets/Scripts/Runtime/Foo.cs").write_text("namespace NPCSystem { public class Foo { public void Bar() {} } }")
    (tmp_path / "Packages").mkdir()
    (tmp_path / "Packages/manifest.json").write_text(json.dumps({"dependencies":{"com.unity.test-framework":"1.7.0"}}))

    result = build_index(CodebaseEmbedderConfig(project_root=tmp_path), write_artifacts=True)

    assert result.counts["csharp_files"] == 1
    assert result.counts["asmdef_files"] == 1
    assert (tmp_path / ".codebase-index/chunks.jsonl").exists()
    assert any(r.record_type == "type" and r.payload["type_name"] == "Foo" for r in result.records)
    assert any(r.record_type == "namespace" and r.payload["namespace"] == "NPCSystem" for r in result.records)


def test_structural_query_prefers_namespace_records(tmp_path: Path):
    (tmp_path / "Assets/Scripts/Runtime").mkdir(parents=True)
    (tmp_path / "Assets/Scripts/Runtime/NPCSystem.Runtime.asmdef").write_text(json.dumps({"name": "NPCSystem.Runtime", "rootNamespace": "NPCSystem", "references": []}))
    (tmp_path / "Assets/Scripts/Runtime/Foo.cs").write_text(
        "using UnityEngine;\n"
        "using LLMUnity;\n"
        "namespace NPCSystem.Dialogue { public class Foo { public void Bar() {} } }\n"
    )
    (tmp_path / "Packages").mkdir()
    (tmp_path / "Packages/manifest.json").write_text(json.dumps({"dependencies": {"com.unity.test-framework": "1.7.0"}}))

    cfg = CodebaseEmbedderConfig(project_root=tmp_path)
    build_index(cfg, write_artifacts=True)
    results = lexical_query(cfg, "list all namespaces and references in the project", limit=5)

    assert results
    assert results[0]["payload"]["record_type"] in {"namespace", "using_directive", "file_overview", "assembly", "relation"}


def test_index_record_point_id_is_stable_across_content_changes():
    first = IndexRecord("type", "type:NPCSystem.Foo:Assets/Scripts/Foo.cs", "old text")
    second = IndexRecord("type", "type:NPCSystem.Foo:Assets/Scripts/Foo.cs", "new text")

    assert first.point_id == second.point_id



def test_owner_query_prefers_type_over_member_for_implemented_prompt(tmp_path: Path):
    (tmp_path / "Assets/LLMUnity/Runtime").mkdir(parents=True)
    (tmp_path / "Assets/LLMUnity/Runtime/undream.llmunity.Runtime.asmdef").write_text(json.dumps({"name": "undream.llmunity.Runtime", "rootNamespace": "LLMUnity", "references": []}))
    (tmp_path / "Assets/LLMUnity/Runtime/LLMClient.cs").write_text(
        "namespace LLMUnity { public class LLMClient { public void Register() {} public void GetNumClients() {} } }"
    )

    cfg = CodebaseEmbedderConfig(project_root=tmp_path)
    build_index(cfg, write_artifacts=True)
    results = lexical_query(cfg, "where is llm client implemented", limit=5)

    assert results
    assert results[0]["payload"]["record_type"] == "type"
    assert results[0]["payload"]["type_name"] == "LLMClient"


def test_query_response_includes_workflow_metadata(tmp_path: Path):
    (tmp_path / "Assets/Scripts/Runtime").mkdir(parents=True)
    (tmp_path / "Assets/Scripts/Runtime/NPCSystem.Runtime.asmdef").write_text(json.dumps({"name":"NPCSystem.Runtime","rootNamespace":"NPCSystem","references":[]}))
    (tmp_path / "Assets/Scripts/Runtime/Foo.cs").write_text("namespace NPCSystem { public class Foo { public void Bar() {} } }")
    (tmp_path / "Packages").mkdir()
    (tmp_path / "Packages/manifest.json").write_text(json.dumps({"dependencies":{"com.unity.test-framework":"1.7.0"}}))

    cfg = CodebaseEmbedderConfig(project_root=tmp_path)
    build_index(cfg, write_artifacts=True)
    response = build_query_response(cfg, "which scripts reference Foo", limit=3, local=True)

    assert response["workflow"]["query_class"] == "structural"
    assert response["workflow"]["preferred_sources"][0] == "structural_records"
    assert response["results"]


def test_format_query_workflow_mentions_mcp_for_scene_questions():
    text = format_query_workflow("which scene objects use Qdrant")

    assert "scene_integration" in text
    assert "gladekit_mcp_scene_hierarchy" in text


def test_write_timing_report_creates_machine_readable_json(tmp_path: Path):
    path = tmp_path / "timings" / "scan.json"
    cfg = CodebaseEmbedderConfig(project_root=tmp_path, collection_name="benchmark_collection", collection_profile="runtime")

    write_timing_report(
        path,
        command="scan",
        config=cfg,
        timings={"build_index": 0.125},
        counts={"records": 3, "chunks": 2},
        extra={"cache_hits": 1},
    )

    data = json.loads(path.read_text())
    assert data["command"] == "scan"
    assert data["collection"] == "benchmark_collection"
    assert data["profile"] == "runtime"
    assert data["counts"] == {"chunks": 2, "records": 3}
    assert data["timings_seconds"]["build_index"] == 0.125
    assert data["cache_hits"] == 1


def test_embed_records_with_cache_skips_unchanged_records(tmp_path: Path):
    records = [
        IndexRecord("type", "type:A", "same text"),
        IndexRecord("type", "type:B", "new text"),
    ]
    calls: list[list[str]] = []

    class FakeEmbeddingClient:
        model = "fake-embedder"

        def embed(self, texts: list[str]) -> list[list[float]]:
            calls.append(texts)
            return [[float(len(text)), 0.0] for text in texts]

    first_vectors, first_stats = embed_records_with_cache(records, FakeEmbeddingClient(), tmp_path, 2, batch_size=2, use_cache=True)
    second_vectors, second_stats = embed_records_with_cache(records, FakeEmbeddingClient(), tmp_path, 2, batch_size=2, use_cache=True)

    assert first_vectors == second_vectors
    assert first_stats["cache_misses"] == 2
    assert second_stats["cache_hits"] == 2
    assert second_stats["cache_misses"] == 0
    assert calls == [["same text", "new text"]]
