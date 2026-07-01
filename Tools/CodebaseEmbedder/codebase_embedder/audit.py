from __future__ import annotations

import json
import re
from collections import Counter
from pathlib import Path
from typing import Any

from .config import CodebaseEmbedderConfig
from .query import lexical_query, qdrant_query
from .workflow import MCP_PREPHASE_TOOLING, build_workflow_plan


SCENARIOS: dict[str, list[str]] = {
    "localai-llmunity": [
        "which llmunity script handles localai backend requests",
        "where is llm client implemented",
        "where is qdrant rag search implemented",
        "which script manages embeddings for llm rag",
        "where is cognee memory service implemented",
    ],
}

FOCUS_COMPONENTS = {
    "NPCDialogueManager",
    "NPCDialogueBootstrapper",
    "NPCDialogueSmokeValidator",
    "QdrantRAGService",
    "CogneeMemoryService",
    "FunctionCalling",
    "LLMAgent",
    "LLM",
}

BUILTIN_COMPONENT_TYPES = {
    4: "Transform",
    20: "Camera",
    21: "Material",
    23: "MeshRenderer",
    33: "MeshFilter",
    65: "BoxCollider",
    82: "AudioSource",
    108: "Light",
    114: "MonoBehaviour",
    224: "RectTransform",
}


def audit_project(
    config: CodebaseEmbedderConfig,
    script_path: str | None = None,
    prompts: list[str] | None = None,
    scenario: str | None = None,
    use_qdrant: bool = False,
    limit: int = 5,
    scene_path: str | None = None,
) -> dict[str, Any]:
    artifact_dir = config.artifact_dir
    manifest = _load_json(artifact_dir / "manifest.json")
    symbols = _load_jsonl(artifact_dir / "symbols.jsonl")
    relations = _load_jsonl(artifact_dir / "relations.jsonl")
    asmdefs = _load_json(artifact_dir / "asmdefs.json")

    prompt_list = list(prompts or [])
    if scenario:
        prompt_list.extend(SCENARIOS.get(scenario, []))
    if not prompt_list:
        prompt_list = [
            "where is qdrant rag search implemented",
            "which llmunity script handles localai backend requests",
        ]

    unique_prompts: list[str] = []
    seen_prompts: set[str] = set()
    for prompt in prompt_list:
        if prompt not in seen_prompts:
            seen_prompts.add(prompt)
            unique_prompts.append(prompt)

    report: dict[str, Any] = {
        "project": manifest.get("project", config.project_slug),
        "collection": manifest.get("collection", config.collection_name),
        "counts": manifest.get("counts", {}),
        "asmdef_count": len(asmdefs),
        "top_assemblies": _top_assemblies(symbols),
        "smoke_queries": _run_smoke_queries(config, unique_prompts, use_qdrant, limit),
    }
    if script_path:
        report["script"] = _audit_script(script_path, symbols, relations)
    if scene_path:
        report["scene"] = _audit_scene(config, scene_path)
    report["workflow"] = build_workflow_plan(unique_prompts, scenario, scene_path, report.get("scene"))
    report["insights"] = _scenario_insights(report["smoke_queries"], scenario, report.get("scene"))
    return report


def format_audit_report(report: dict[str, Any]) -> str:
    lines = [
        f"Project: {report['project']}",
        f"Collection: {report['collection']}",
        "Counts:",
    ]
    for key, value in sorted(report.get("counts", {}).items()):
        lines.append(f"- {key}: {value}")
    lines.append(f"- asmdef_count: {report.get('asmdef_count', 0)}")
    script = report.get("script")
    if script:
        lines += [
            "",
            f"Script audit: {script['path']}",
            f"- asmdefs: {', '.join(script['asmdefs']) or '-'}",
            f"- namespaces ({script['namespace_count']}): {', '.join(script['namespaces']) or '-'}",
            f"- related_namespaces ({len(script['related_namespaces'])}): {', '.join(script['related_namespaces']) or '-'}",
            f"- types: {script['type_count']}",
            f"- members: {script['member_count']}",
            f"- serialized_fields: {script['serialized_field_count']}",
            f"- relation_counts: {json.dumps(script['relation_counts'], sort_keys=True)}",
        ]

    scene = report.get("scene")
    if scene:
        lines += ["", f"Scene audit: {scene['path']}"]
        if scene.get("error"):
            lines.append(f"- error: {scene['error']}")
        else:
            lines.append(f"- gameobject_count: {scene['gameobject_count']}")
            lines.append(f"- hotspot_count: {len(scene.get('hotspots', []))}")
            transport = scene.get("transport", {})
            if transport:
                lines.append(f"- transport: {transport.get('summary', '-')}")
            hotspot_lines = scene.get("hotspots", [])
            if hotspot_lines:
                lines.append("- hotspots:")
                for hotspot in hotspot_lines:
                    component_summary = ", ".join(hotspot.get("focus_components", [])) or ", ".join(hotspot.get("components", [])) or "-"
                    lines.append(f"  - {hotspot['game_object']} ({component_summary})")
            scene_warnings = scene.get("warnings", [])
            if scene_warnings:
                lines.append("- scene_warnings:")
                lines.extend(f"  - {warning}" for warning in scene_warnings)
            scene_recommendations = scene.get("recommendations", [])
            if scene_recommendations:
                lines.append("- scene_recommendations:")
                lines.extend(f"  - {recommendation}" for recommendation in scene_recommendations)

    workflow = report.get("workflow")
    if workflow:
        pre_phase = workflow.get("pre_phase", {})
        lines += ["", "Workflow:"]
        lines.append(f"- strategy: {pre_phase.get('strategy', '-')}")
        lines.append(f"- rationale: {pre_phase.get('rationale', '-')}")
        lines.append(f"- live_scene_required: {pre_phase.get('live_scene_required', False)}")
        mcp_tools = pre_phase.get("mcp_tools", [])
        if mcp_tools:
            lines.append(f"- mcp_tools: {', '.join(mcp_tools)}")
        scene_targets = pre_phase.get("scene_targets", [])
        if scene_targets:
            lines.append(f"- scene_targets: {', '.join(scene_targets)}")
        query_plans = workflow.get("queries", [])
        if query_plans:
            lines.append("- query_plan:")
            for plan in query_plans:
                lines.append(
                    f"  - {plan['prompt']} [{plan['query_class']}] -> "
                    f"{', '.join(plan['preferred_sources'])}"
                )

    lines += ["", "Smoke queries:"]
    for item in report.get("smoke_queries", []):
        lines.append(f"- {item['prompt']}")
        lines.append(f"  top_hit: {item['top_hit_path']}")
        lines.append(f"  top_symbol: {item['top_symbol']}")
        lines.append(f"  top_score: {item['top_score']:.3f}")
    insights = report.get("insights", {})
    if insights:
        lines += ["", "Insights:"]
        for label in ["candidate_paths", "strengths", "warnings", "recommendations"]:
            values = insights.get(label) or []
            if not values:
                continue
            lines.append(f"- {label}:")
            lines.extend(f"  - {value}" for value in values)
    return "\n".join(lines)


def _audit_script(script_path: str, symbols: list[dict[str, Any]], relations: list[dict[str, Any]]) -> dict[str, Any]:
    symbol_rows = [row for row in symbols if row.get("payload", {}).get("path") == script_path]
    relation_rows = [row for row in relations if row.get("path") == script_path]

    namespaces = sorted({row.get("payload", {}).get("namespace") for row in symbol_rows if row.get("payload", {}).get("namespace")})
    asmdefs = sorted({row.get("payload", {}).get("asmdef") for row in symbol_rows if row.get("payload", {}).get("asmdef")})
    related_namespaces = sorted({
        ns
        for row in relation_rows
        for ns in (_namespace_from_symbol(row.get("source", "")), _namespace_from_symbol(row.get("target", "")))
        if ns
    })
    relation_counts = Counter(row.get("relation_kind", "unknown") for row in relation_rows)

    return {
        "path": script_path,
        "asmdefs": asmdefs,
        "namespaces": namespaces,
        "namespace_count": len(namespaces),
        "related_namespaces": related_namespaces,
        "type_count": sum(1 for row in symbol_rows if row.get("record_type") == "type"),
        "member_count": sum(1 for row in symbol_rows if row.get("record_type") == "member"),
        "serialized_field_count": sum(1 for row in symbol_rows if row.get("record_type") == "serialized_field"),
        "relation_counts": dict(sorted(relation_counts.items())),
    }


def _audit_scene(config: CodebaseEmbedderConfig, scene_path: str) -> dict[str, Any]:
    scene_file = _resolve_project_path(Path(config.project_root), scene_path)
    scene_report: dict[str, Any] = {"path": scene_path}
    if not scene_file.exists():
        scene_report["error"] = f"Scene not found: {scene_path}"
        scene_report["warnings"] = [scene_report["error"]]
        scene_report["recommendations"] = ["Pass --scene with an existing Assets/... .unity file to correlate code and scene wiring."]
        return scene_report

    script_guid_map = _build_script_guid_map(Path(config.project_root))
    sections = _parse_scene_sections(scene_file.read_text(encoding="utf-8"))
    game_objects = _collect_scene_gameobjects(sections)
    components = _collect_scene_components(sections, script_guid_map)
    _attach_component_summaries(game_objects, components)

    hotspots = _scene_hotspots(game_objects)
    transport = _scene_transport_summary(components)
    warnings, recommendations = _scene_warnings_and_recommendations(components, transport)
    component_paths = sorted({
        component.get("script_path", "")
        for component in components.values()
        if component.get("script_path") and component.get("component_type") in FOCUS_COMPONENTS
    })

    scene_report.update({
        "gameobject_count": len(game_objects),
        "hotspots": hotspots,
        "transport": transport,
        "warnings": warnings,
        "recommendations": recommendations,
        "component_paths": component_paths,
    })
    return scene_report


def _run_smoke_queries(config: CodebaseEmbedderConfig, prompts: list[str], use_qdrant: bool, limit: int) -> list[dict[str, Any]]:
    query_fn = qdrant_query if use_qdrant else lexical_query
    rows = []
    for prompt in prompts:
        results = query_fn(config, prompt, limit)
        top = results[0] if results else {"score": 0.0, "payload": {}}
        payload = top.get("payload", {})
        rows.append({
            "prompt": prompt,
            "top_hit_path": payload.get("path", ""),
            "top_symbol": payload.get("member_name") or payload.get("type_name") or payload.get("heading") or payload.get("record_type", ""),
            "top_score": float(top.get("score", 0.0)),
        })
    return rows


def _top_assemblies(symbols: list[dict[str, Any]], limit: int = 5) -> list[dict[str, Any]]:
    counts = Counter(row.get("payload", {}).get("asmdef", "") for row in symbols if row.get("payload", {}).get("asmdef"))
    return [{"asmdef": asmdef, "symbol_count": count} for asmdef, count in counts.most_common(limit)]


def _scenario_insights(smoke_queries: list[dict[str, Any]], scenario: str | None, scene_report: dict[str, Any] | None = None) -> dict[str, Any]:
    candidate_paths: list[str] = []
    strengths: list[str] = []
    warnings: list[str] = []
    for item in smoke_queries:
        path = item.get("top_hit_path", "")
        if path and path not in candidate_paths:
            candidate_paths.append(path)
        prompt = item.get("prompt", "")
        if path.endswith(".md") or "/Editor/" in path:
            warnings.append(f"Prompt '{prompt}' surfaced non-runtime/documentation-first hit: {path}")
        if "QdrantRAGService.cs" in path:
            strengths.append("Qdrant/NPC retrieval prompt resolves to NPC runtime integration code.")
        if "LLMClient.cs" in path or "LLMUnitySetup.cs" in path:
            strengths.append("LLMUnity/LocalAI prompts resolve to runtime/backend-adjacent scripts.")
        if "CogneeMemoryService.cs" in path:
            strengths.append("Memory integration prompt resolves to project-specific Cognee bridge code.")

    recommendations: list[str]
    if scenario == "localai-llmunity":
        if not strengths:
            warnings.append("LocalAI + LLMUnity scenario did not surface obvious runtime integration hotspots.")
        recommendations = [
            "Inspect runtime hits first: LLMClient/LLMUnitySetup/QdrantRAGService/CogneeMemoryService before touching editor utilities.",
            "Use asmdef boundaries to keep LocalAI backend changes inside Runtime assemblies unless the change is truly editor tooling.",
            "If documentation outranks runtime code for backend prompts, improve summaries or add explicit backend-focused records for those scripts.",
        ]
    else:
        recommendations = []

    if scene_report and not scene_report.get("error"):
        for path in scene_report.get("component_paths", []):
            if path and path not in candidate_paths:
                candidate_paths.append(path)
        transport = scene_report.get("transport", {})
        if transport.get("localai_direct_http"):
            strengths.append("Scene transport is wired for direct LocalAI HTTP from NPCDialogueManager.")
        if transport.get("shared_transport_is_local"):
            strengths.append("Shared NPC LLMAgent/LLM remote flags are disabled, matching direct LocalAI HTTP architecture.")
        if transport.get("function_calling_dedicated_agent"):
            strengths.append("FunctionCalling uses a dedicated selector agent instead of sharing the NPC dialogue LLMAgent.")
        warnings.extend(scene_report.get("warnings", []))
        recommendations.extend(scene_report.get("recommendations", []))
        recommendations.append("Use GladeKit MCP scene/component inspection before code-only retrieval when answering wiring or inspector-state questions.")

    return {
        "candidate_paths": candidate_paths,
        "strengths": sorted(set(strengths)),
        "warnings": warnings,
        "recommendations": list(dict.fromkeys(recommendations)),
    }


def _namespace_from_symbol(symbol: str) -> str | None:
    if not symbol or "." not in symbol:
        return None
    parts = symbol.split(".")
    if len(parts) < 2:
        return None
    if any(not part or not part[0].isupper() for part in parts[:-1]):
        return None
    if len(parts) == 2:
        return None
    return ".".join(parts[:-2]) if parts[-1][:1].isupper() and parts[-2][:1].isupper() else ".".join(parts[:-1])



def _resolve_project_path(project_root: Path, project_relative_path: str) -> Path:
    candidate = Path(project_relative_path)
    return candidate if candidate.is_absolute() else project_root / candidate


def _build_script_guid_map(project_root: Path) -> dict[str, dict[str, str]]:
    mapping: dict[str, dict[str, str]] = {}
    for meta_path in project_root.rglob("*.cs.meta"):
        text = meta_path.read_text(encoding="utf-8")
        match = re.search(r"^guid:\s*([0-9a-f]+)\s*$", text, flags=re.MULTILINE)
        if not match:
            continue
        script_path = meta_path.with_suffix("")
        mapping[match.group(1)] = {
            "script_path": script_path.relative_to(project_root).as_posix(),
            "component_type": script_path.stem,
        }
    return mapping


def _parse_scene_sections(scene_text: str) -> list[dict[str, Any]]:
    parts = re.split(r"(?m)^--- !u!(\d+) &(\-?\d+)\n", scene_text)
    sections: list[dict[str, Any]] = []
    for index in range(1, len(parts), 3):
        class_id = int(parts[index])
        file_id = int(parts[index + 1])
        body = parts[index + 2]
        sections.append({"class_id": class_id, "file_id": file_id, "body": body})
    return sections


def _collect_scene_gameobjects(sections: list[dict[str, Any]]) -> dict[int, dict[str, Any]]:
    game_objects: dict[int, dict[str, Any]] = {}
    for section in sections:
        if section["class_id"] != 1:
            continue
        body = section["body"]
        game_objects[section["file_id"]] = {
            "file_id": section["file_id"],
            "name": _extract_scalar(body, "m_Name") or f"GameObject_{section['file_id']}",
            "active": _extract_scalar(body, "m_IsActive") != "0",
            "component_ids": [int(file_id) for file_id in re.findall(r"(?m)^  - component: \{fileID: (\-?\d+)\}", body)],
            "components": [],
            "focus_components": [],
        }
    return game_objects


def _collect_scene_components(sections: list[dict[str, Any]], script_guid_map: dict[str, dict[str, str]]) -> dict[int, dict[str, Any]]:
    components: dict[int, dict[str, Any]] = {}
    for section in sections:
        class_id = section["class_id"]
        if class_id not in BUILTIN_COMPONENT_TYPES and class_id != 114:
            continue
        body = section["body"]
        game_object_id = _extract_fileid(body, "m_GameObject")
        component: dict[str, Any] = {
            "file_id": section["file_id"],
            "class_id": class_id,
            "game_object_id": game_object_id,
            "fields": _parse_top_level_fields(body),
        }
        if class_id == 114:
            script_guid = _extract_script_guid(body)
            script_info = script_guid_map.get(script_guid or "", {})
            component["script_guid"] = script_guid
            component["component_type"] = script_info.get("component_type") or "MonoBehaviour"
            component["script_path"] = script_info.get("script_path", "")
        else:
            component["component_type"] = BUILTIN_COMPONENT_TYPES.get(class_id, f"Class{class_id}")
            component["script_path"] = ""
        components[section["file_id"]] = component
    return components


def _attach_component_summaries(game_objects: dict[int, dict[str, Any]], components: dict[int, dict[str, Any]]) -> None:
    for game_object in game_objects.values():
        component_names: list[str] = []
        focus_components: list[str] = []
        for component_id in game_object.get("component_ids", []):
            component = components.get(component_id)
            if not component:
                continue
            component["game_object_name"] = game_object["name"]
            component_names.append(component.get("component_type", "Unknown"))
            if component.get("component_type") in FOCUS_COMPONENTS:
                focus_components.append(component["component_type"])
        game_object["components"] = component_names
        game_object["focus_components"] = focus_components


def _scene_hotspots(game_objects: dict[int, dict[str, Any]]) -> list[dict[str, Any]]:
    hotspots = [
        {
            "game_object": game_object["name"],
            "active": game_object["active"],
            "components": game_object.get("components", []),
            "focus_components": game_object.get("focus_components", []),
        }
        for game_object in game_objects.values()
        if game_object.get("focus_components")
    ]
    hotspots.sort(key=lambda item: (0 if "NPCDialogueManager" in item["focus_components"] else 1, item["game_object"]))
    return hotspots


def _scene_transport_summary(components: dict[int, dict[str, Any]]) -> dict[str, Any]:
    npc_manager = _find_component(components, "NPCDialogueManager")
    shared_agent = _find_component(components, "LLMAgent", game_object_name="LLMAgent")
    shared_llm = _find_component(components, "LLM", game_object_name="LLM")
    function_calling = _find_component(components, "FunctionCalling")

    npc_fields = npc_manager.get("fields", {}) if npc_manager else {}
    shared_agent_fields = shared_agent.get("fields", {}) if shared_agent else {}
    shared_llm_fields = shared_llm.get("fields", {}) if shared_llm else {}
    function_fields = function_calling.get("fields", {}) if function_calling else {}

    function_agent_ref = _resolve_component_reference(components, _parse_fileid_value(function_fields.get("llmAgent")))
    npc_agent_ref = _resolve_component_reference(components, _parse_fileid_value(npc_fields.get("llmAgent")))
    npc_llm_ref = _resolve_component_reference(components, _parse_fileid_value(npc_fields.get("llm")))

    use_remote_server = _parse_boolish(npc_fields.get("useRemoteServer"))
    shared_agent_remote = _parse_boolish(shared_agent_fields.get("_remote"))
    shared_llm_remote = _parse_boolish(shared_llm_fields.get("_remote"))

    summary_parts: list[str] = []
    if use_remote_server:
        summary_parts.append(
            f"NPC dialogue targets LocalAI at {npc_fields.get('remoteHost', '-') }:{npc_fields.get('remotePort', '-')} model {npc_fields.get('remoteModel', '-')}."
        )
    if shared_agent is not None:
        summary_parts.append(f"Shared LLMAgent.remote={_bool_label(shared_agent_remote)}.")
    if shared_llm is not None:
        summary_parts.append(f"Shared LLM.remote={_bool_label(shared_llm_remote)}.")
    if function_agent_ref:
        summary_parts.append(f"FunctionCalling.llmAgent -> {function_agent_ref.get('game_object', '-')}/{function_agent_ref.get('component_type', '-') }.")

    return {
        "backend": "LocalAI" if use_remote_server else "LLMUnity-local",
        "localai_direct_http": use_remote_server,
        "npc_remote_host": npc_fields.get("remoteHost", ""),
        "npc_remote_port": npc_fields.get("remotePort", ""),
        "npc_remote_model": npc_fields.get("remoteModel", ""),
        "use_qdrant_rag": _parse_boolish(npc_fields.get("useQdrantRag")),
        "use_cognee_memory": _parse_boolish(npc_fields.get("useCogneeMemory")),
        "shared_llm_agent_remote": shared_agent_remote,
        "shared_llm_remote": shared_llm_remote,
        "shared_transport_is_local": not shared_agent_remote and not shared_llm_remote,
        "function_calling_agent_game_object": function_agent_ref.get("game_object", "") if function_agent_ref else "",
        "function_calling_agent_component": function_agent_ref.get("component_type", "") if function_agent_ref else "",
        "function_calling_dedicated_agent": bool(function_agent_ref and function_agent_ref.get("game_object") == "FunctionCallingAgent"),
        "function_calling_shares_npc_agent": bool(function_agent_ref and npc_agent_ref and function_agent_ref.get("file_id") == npc_agent_ref.get("file_id")),
        "npc_llm_game_object": npc_llm_ref.get("game_object", "") if npc_llm_ref else "",
        "summary": " ".join(part for part in summary_parts if part),
    }


def _scene_warnings_and_recommendations(components: dict[int, dict[str, Any]], transport: dict[str, Any]) -> tuple[list[str], list[str]]:
    warnings: list[str] = []
    recommendations: list[str] = []

    npc_manager = _find_component(components, "NPCDialogueManager")
    function_calling = _find_component(components, "FunctionCalling")
    npc_fields = npc_manager.get("fields", {}) if npc_manager else {}
    function_fields = function_calling.get("fields", {}) if function_calling else {}

    if transport.get("localai_direct_http") and transport.get("shared_llm_agent_remote"):
        warnings.append("NPCDialogueManager uses direct LocalAI HTTP, but the shared LLMAgent still has remote enabled.")
    if transport.get("localai_direct_http") and transport.get("shared_llm_remote"):
        warnings.append("NPCDialogueManager uses direct LocalAI HTTP, but the shared LLM still has remote enabled.")
    if transport.get("function_calling_shares_npc_agent"):
        warnings.append("FunctionCalling still shares the NPC dialogue LLMAgent instead of using a dedicated selector agent.")
    if function_calling and not transport.get("function_calling_dedicated_agent"):
        warnings.append("FunctionCalling is not wired to FunctionCallingAgent.")

    if npc_manager:
        if _parse_boolish(npc_fields.get("useQdrantRag")) and _parse_fileid_value(npc_fields.get("qdrantRag")) == 0:
            warnings.append("NPCDialogueManager has useQdrantRag enabled but no QdrantRAGService reference assigned.")
        if _parse_boolish(npc_fields.get("useCogneeMemory")) and _parse_fileid_value(npc_fields.get("cogneeMemory")) == 0:
            warnings.append("NPCDialogueManager has useCogneeMemory enabled but no CogneeMemoryService reference assigned.")
        if _parse_boolish(npc_fields.get("useRemoteServer")) and not str(npc_fields.get("remoteModel", "")).strip():
            warnings.append("NPCDialogueManager uses a remote backend but remoteModel is blank.")

    if function_calling:
        for field_name in ["llmAgent", "npcDialogueManager", "playerText", "AIText"]:
            if _parse_fileid_value(function_fields.get(field_name)) == 0:
                warnings.append(f"FunctionCalling is missing its '{field_name}' scene reference.")

    if not transport.get("localai_direct_http"):
        recommendations.append("If NPC dialogue should use LocalAI over OpenAI-compatible HTTP, enable useRemoteServer and point it at localhost:8080.")
    if transport.get("localai_direct_http") and not transport.get("shared_transport_is_local"):
        recommendations.append("Keep shared NPC LLMAgent/LLM remote flags disabled when NPCDialogueManager talks to LocalAI directly over HTTP.")
    if function_calling and not transport.get("function_calling_dedicated_agent"):
        recommendations.append("Wire FunctionCalling.llmAgent to a dedicated FunctionCallingAgent so sample function selection does not share NPC dialogue transport assumptions.")
    if npc_manager and _parse_boolish(npc_fields.get("useQdrantRag")):
        recommendations.append("Treat QdrantRAGService as a first-class runtime hotspot when adjusting retrieval quality or NPC grounding.")
    if npc_manager and _parse_boolish(npc_fields.get("useCogneeMemory")):
        recommendations.append("Include CogneeMemoryService in dialogue audits when memory behavior or project knowledge influences NPC responses.")

    return warnings, list(dict.fromkeys(recommendations))


def _find_component(
    components: dict[int, dict[str, Any]],
    component_type: str,
    game_object_name: str | None = None,
) -> dict[str, Any] | None:
    for component in components.values():
        if component.get("component_type") != component_type:
            continue
        if game_object_name and component.get("game_object_name") != game_object_name:
            continue
        return component
    return None


def _resolve_component_reference(components: dict[int, dict[str, Any]], file_id: int) -> dict[str, Any] | None:
    if not file_id:
        return None
    component = components.get(file_id)
    if not component:
        return {"file_id": file_id, "game_object": "<missing>", "component_type": "<missing>"}
    return {
        "file_id": file_id,
        "game_object": component.get("game_object_name", ""),
        "component_type": component.get("component_type", ""),
        "script_path": component.get("script_path", ""),
    }


def _extract_scalar(body: str, key: str) -> str:
    match = re.search(rf"(?m)^  {re.escape(key)}:(?:\s*(.*))?$", body)
    return (match.group(1) or "").strip() if match else ""


def _extract_fileid(body: str, key: str) -> int:
    match = re.search(rf"(?m)^  {re.escape(key)}: \{{fileID: (\-?\d+)\}}$", body)
    return int(match.group(1)) if match else 0


def _extract_script_guid(body: str) -> str | None:
    match = re.search(r"(?m)^  m_Script: \{fileID: 11500000, guid: ([0-9a-f]+), type: 3\}$", body)
    return match.group(1) if match else None


def _parse_top_level_fields(body: str) -> dict[str, str]:
    fields: dict[str, str] = {}
    for line in body.splitlines():
        match = re.match(r"^  ([A-Za-z_][A-Za-z0-9_]*):(?:\s*(.*))?$", line)
        if not match:
            continue
        key, value = match.groups()
        fields[key] = (value or "").strip()
    return fields


def _parse_fileid_value(value: Any) -> int:
    if value is None:
        return 0
    text = str(value)
    match = re.search(r"\{fileID: (\-?\d+)\}", text)
    if match:
        return int(match.group(1))
    if text.lstrip("-").isdigit():
        return int(text)
    return 0


def _parse_boolish(value: Any) -> bool:
    if isinstance(value, bool):
        return value
    text = str(value or "").strip().lower()
    return text in {"1", "true", "yes"}


def _bool_label(value: bool) -> str:
    return "true" if value else "false"


def _load_json(path: Path) -> Any:
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def _load_jsonl(path: Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]
