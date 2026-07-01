import json
from pathlib import Path

from codebase_embedder.audit import audit_project
from codebase_embedder.config import CodebaseEmbedderConfig
from codebase_embedder.indexer import build_index


NPC_DIALOGUE_GUID = "32852d652d4bd17afa0f0b195a43165b"
QDRANT_GUID = "307aa1d34162b1b759aeeac4d0b0ba14"
COGNEE_GUID = "ccd5b467aad27e2f1ba9dfd88623492e"
FUNCTION_CALLING_GUID = "162e77d443c2df77b8297c1dcdfbfdce"
LLM_AGENT_GUID = "b4326d5ae3b03ff55847035351559f4e"
LLM_GUID = "a1111111111111111111111111111111"
INPUT_FIELD_GUID = "b1111111111111111111111111111111"
TEXT_GUID = "c1111111111111111111111111111111"


def _write_script(path: Path, content: str, guid: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content)
    path.with_suffix(path.suffix + ".meta").write_text(f"fileFormatVersion: 2\nguid: {guid}\n")


def _seed_project(tmp_path: Path) -> CodebaseEmbedderConfig:
    (tmp_path / "Assets/Scripts/Runtime").mkdir(parents=True)
    (tmp_path / "Assets/Scripts/Runtime/NPCSystem.Runtime.asmdef").write_text(json.dumps({
        "name": "NPCSystem.Runtime",
        "rootNamespace": "NPCSystem",
        "references": ["undream.llmunity.Runtime"],
    }))
    (tmp_path / "Assets/LLMUnity/Runtime").mkdir(parents=True)
    (tmp_path / "Assets/LLMUnity/Runtime/undream.llmunity.Runtime.asmdef").write_text(json.dumps({
        "name": "undream.llmunity.Runtime",
        "rootNamespace": "LLMUnity",
        "references": [],
    }))

    _write_script(
        tmp_path / "Assets/Scripts/Runtime/NPCDialogueManager.cs",
        "namespace NPCSystem { public class NPCDialogueManager { public void SendToLocalAIAsync() {} } }",
        NPC_DIALOGUE_GUID,
    )
    _write_script(
        tmp_path / "Assets/Scripts/Runtime/NPCDialogue/QdrantRAGService.cs",
        "namespace NPCSystem { public class QdrantRAGService { public void SearchMemoryAsync() {} } }",
        QDRANT_GUID,
    )
    _write_script(
        tmp_path / "Assets/LLMUnity/Scripts/CogneeMemoryService.cs",
        "namespace GladeAgenticAI.Core.Memory { public class CogneeMemoryService { public void Sync() {} } }",
        COGNEE_GUID,
    )
    _write_script(
        tmp_path / "Assets/LLMUnity/Samples/FunctionCalling/FunctionCalling.cs",
        "namespace LLMUnitySamples { public class FunctionCalling { public void BuildFunctions() {} } }",
        FUNCTION_CALLING_GUID,
    )
    _write_script(
        tmp_path / "Assets/LLMUnity/Runtime/LLMAgent.cs",
        "namespace LLMUnity { public class LLMAgent { public void Chat() {} } }",
        LLM_AGENT_GUID,
    )
    _write_script(
        tmp_path / "Assets/LLMUnity/Runtime/LLM.cs",
        "namespace LLMUnity { public class LLM { public void Complete() {} } }",
        LLM_GUID,
    )
    _write_script(
        tmp_path / "Assets/UI/InputField.cs",
        "public class InputField { }",
        INPUT_FIELD_GUID,
    )
    _write_script(
        tmp_path / "Assets/UI/Text.cs",
        "public class Text { }",
        TEXT_GUID,
    )
    _write_script(
        tmp_path / "Assets/LLMUnity/Runtime/LLMClient.cs",
        "namespace LLMUnity { public class LLMClient { public void Send() {} } }",
        "d1111111111111111111111111111111",
    )

    (tmp_path / "Packages").mkdir()
    (tmp_path / "Packages/manifest.json").write_text(json.dumps({
        "dependencies": {"com.unity.test-framework": "1.7.0"}
    }))

    (tmp_path / "Assets/Scenes").mkdir(parents=True)
    (tmp_path / "Assets/Scenes/NPCDialoguePrototype.unity").write_text(_scene_yaml())

    cfg = CodebaseEmbedderConfig(project_root=tmp_path)
    build_index(cfg, write_artifacts=True)
    return cfg


def _scene_yaml() -> str:
    return """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &100
GameObject:
  m_ObjectHideFlags: 0
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 101}
  - component: {fileID: 102}
  - component: {fileID: 103}
  - component: {fileID: 104}
  m_Layer: 0
  m_Name: NPCDialogueSystem
  m_TagString: Untagged
  m_IsActive: 1
--- !u!4 &101
Transform:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 100}
--- !u!114 &102
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 100}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: 32852d652d4bd17afa0f0b195a43165b, type: 3}
  m_Name:
  m_EditorClassIdentifier:
  llm: {fileID: 302}
  llmAgent: {fileID: 202}
  qdrantRag: {fileID: 103}
  useQdrantRag: 1
  useRemoteServer: 1
  remoteHost: localhost
  remotePort: 8080
  remoteModel: openai/gemma-3-4b-it-q4_k_m
  cogneeMemory: {fileID: 104}
  useCogneeMemory: 1
--- !u!114 &103
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 100}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: 307aa1d34162b1b759aeeac4d0b0ba14, type: 3}
  m_Name:
  m_EditorClassIdentifier:
  qdrantUrl: http://localhost:6333
  collectionName: npc_knowledge
--- !u!114 &104
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 100}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: ccd5b467aad27e2f1ba9dfd88623492e, type: 3}
  m_Name:
  m_EditorClassIdentifier:
  CogneeEndpoint: http://localhost:8000/api/v1
--- !u!1 &200
GameObject:
  m_ObjectHideFlags: 0
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 201}
  - component: {fileID: 202}
  m_Layer: 0
  m_Name: LLMAgent
  m_TagString: Untagged
  m_IsActive: 1
--- !u!4 &201
Transform:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 200}
--- !u!114 &202
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 200}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: b4326d5ae3b03ff55847035351559f4e, type: 3}
  m_Name:
  m_EditorClassIdentifier:
  _remote: 0
  _llm: {fileID: 302}
--- !u!1 &300
GameObject:
  m_ObjectHideFlags: 0
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 301}
  - component: {fileID: 302}
  m_Layer: 0
  m_Name: LLM
  m_TagString: Untagged
  m_IsActive: 1
--- !u!4 &301
Transform:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 300}
--- !u!114 &302
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 300}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: a1111111111111111111111111111111, type: 3}
  m_Name:
  m_EditorClassIdentifier:
  _remote: 0
  _model: gemma-3-4b-it-Q4_K_M.gguf
--- !u!1 &400
GameObject:
  m_ObjectHideFlags: 0
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 401}
  - component: {fileID: 402}
  m_Layer: 0
  m_Name: FunctionCalling
  m_TagString: Untagged
  m_IsActive: 1
--- !u!4 &401
Transform:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 400}
--- !u!114 &402
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 400}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: 162e77d443c2df77b8297c1dcdfbfdce, type: 3}
  m_Name:
  m_EditorClassIdentifier:
  llmAgent: {fileID: 502}
  npcDialogueManager: {fileID: 102}
  playerText: {fileID: 702}
  AIText: {fileID: 802}
--- !u!1 &500
GameObject:
  m_ObjectHideFlags: 0
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 501}
  - component: {fileID: 502}
  m_Layer: 0
  m_Name: FunctionCallingAgent
  m_TagString: Untagged
  m_IsActive: 1
--- !u!4 &501
Transform:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 500}
--- !u!114 &502
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 500}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: b4326d5ae3b03ff55847035351559f4e, type: 3}
  m_Name:
  m_EditorClassIdentifier:
  _remote: 1
  _llm: {fileID: 602}
--- !u!1 &600
GameObject:
  m_ObjectHideFlags: 0
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 601}
  - component: {fileID: 602}
  m_Layer: 0
  m_Name: FunctionCallingLLM
  m_TagString: Untagged
  m_IsActive: 1
--- !u!4 &601
Transform:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 600}
--- !u!114 &602
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 600}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: a1111111111111111111111111111111, type: 3}
  m_Name:
  m_EditorClassIdentifier:
  _remote: 1
  _model: gemma-3-4b-it-Q4_K_M.gguf
--- !u!1 &700
GameObject:
  m_ObjectHideFlags: 0
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 701}
  - component: {fileID: 702}
  m_Layer: 5
  m_Name: PlayerInput
  m_TagString: Untagged
  m_IsActive: 1
--- !u!4 &701
Transform:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 700}
--- !u!114 &702
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 700}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: b1111111111111111111111111111111, type: 3}
  m_Name:
  m_EditorClassIdentifier:
--- !u!1 &800
GameObject:
  m_ObjectHideFlags: 0
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 801}
  - component: {fileID: 802}
  m_Layer: 5
  m_Name: AIText
  m_TagString: Untagged
  m_IsActive: 1
--- !u!4 &801
Transform:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 800}
--- !u!114 &802
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {fileID: 800}
  m_Enabled: 1
  m_Script: {fileID: 11500000, guid: c1111111111111111111111111111111, type: 3}
  m_Name:
  m_EditorClassIdentifier:
"""


def test_audit_project_reports_symbol_counts_and_related_namespaces(tmp_path: Path):
    cfg = _seed_project(tmp_path)

    report = audit_project(
        cfg,
        script_path="Assets/Scripts/Runtime/NPCDialogue/QdrantRAGService.cs",
        prompts=["where is qdrant rag search implemented"],
        use_qdrant=False,
    )

    script = report["script"]
    assert script["namespace_count"] == 1
    assert script["type_count"] == 1
    assert script["member_count"] == 1
    assert "NPCSystem" in script["related_namespaces"]
    assert report["smoke_queries"][0]["top_hit_path"] == "Assets/Scripts/Runtime/NPCDialogue/QdrantRAGService.cs"


def test_audit_project_localai_llmunity_scenario_surfaces_llmunity_files(tmp_path: Path):
    cfg = _seed_project(tmp_path)

    report = audit_project(cfg, scenario="localai-llmunity", use_qdrant=False)

    prompts = {item["prompt"]: item for item in report["smoke_queries"]}
    assert any("LLMClient.cs" in item["top_hit_path"] for item in prompts.values())
    assert any("QdrantRAGService.cs" in item["top_hit_path"] for item in prompts.values())
    assert "insights" in report
    assert any("LLMClient.cs" in path for path in report["insights"]["candidate_paths"])
    assert report["workflow"]["pre_phase"]["strategy"] == "gladekit_mcp_first"
    assert "get_scene_hierarchy" in report["workflow"]["pre_phase"]["mcp_tools"]


def test_audit_project_scene_overlay_reports_localai_transport_and_hotspots(tmp_path: Path):
    cfg = _seed_project(tmp_path)

    report = audit_project(
        cfg,
        scenario="localai-llmunity",
        use_qdrant=False,
        scene_path="Assets/Scenes/NPCDialoguePrototype.unity",
    )

    scene = report["scene"]
    assert scene["transport"]["backend"] == "LocalAI"
    assert scene["transport"]["npc_remote_host"] == "localhost"
    assert str(scene["transport"]["npc_remote_port"]) == "8080"
    assert scene["transport"]["npc_remote_model"] == "openai/gemma-3-4b-it-q4_k_m"
    assert scene["transport"]["shared_transport_is_local"] is True
    assert scene["transport"]["function_calling_dedicated_agent"] is True
    assert any(hotspot["game_object"] == "NPCDialogueSystem" for hotspot in scene["hotspots"])
    assert any("QdrantRAGService.cs" in path for path in scene["component_paths"])
    assert any("CogneeMemoryService.cs" in path for path in report["insights"]["candidate_paths"])
    assert any("direct LocalAI HTTP" in strength for strength in report["insights"]["strengths"])
    assert report["workflow"]["pre_phase"]["live_scene_required"] is True
    assert "NPCDialogueSystem" in report["workflow"]["pre_phase"]["scene_targets"]


def test_audit_project_workflow_classifies_query_sources(tmp_path: Path):
    cfg = _seed_project(tmp_path)

    report = audit_project(
        cfg,
        prompts=[
            "which scripts reference LLMAgent",
            "where is LocalAI chat transport implemented",
            "which scene objects use Qdrant",
            "how does the dialogue request reach LocalAI",
        ],
        use_qdrant=False,
        scene_path="Assets/Scenes/NPCDialoguePrototype.unity",
    )

    plans = {item["prompt"]: item for item in report["workflow"]["queries"]}
    assert plans["which scripts reference LLMAgent"]["query_class"] == "structural"
    assert plans["where is LocalAI chat transport implemented"]["query_class"] == "ownership"
    assert plans["which scene objects use Qdrant"]["query_class"] == "scene_integration"
    assert plans["how does the dialogue request reach LocalAI"]["query_class"] == "behavior"
    assert plans["which scene objects use Qdrant"]["preferred_sources"][0] == "gladekit_mcp_scene_hierarchy"
