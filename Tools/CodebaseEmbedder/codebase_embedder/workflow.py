from __future__ import annotations

import re
from typing import Any


QUERY_CLASS_SCENE_INTEGRATION = "scene_integration"
QUERY_CLASS_STRUCTURAL = "structural"
QUERY_CLASS_OWNERSHIP = "ownership"
QUERY_CLASS_BEHAVIOR = "behavior"

SCENE_QUERY_TERMS = {
    "scene", "scenes", "gameobject", "gameobjects", "object", "objects", "component",
    "components", "inspector", "wire", "wiring", "wired",
    "remote", "host", "port", "enabled", "disabled",
}
STRUCTURAL_QUERY_TERMS = {
    "namespace", "namespaces", "asmdef", "asmdefs", "assembly", "assemblies",
    "using", "usings", "reference", "references", "dependency", "dependencies",
    "symbol", "symbols", "class", "classes", "method", "methods", "field", "fields",
    "interface", "interfaces", "enum", "enums", "struct", "structs",
}
OWNERSHIP_QUERY_TERMS = {
    "owner", "owners", "owns", "implemented", "implementation", "script", "scripts",
    "file", "files", "class", "classes", "manager", "service", "controls", "control",
}
BEHAVIOR_QUERY_TERMS = {
    "how", "flow", "flows", "request", "requests", "reach", "reaches", "before",
    "after", "during", "path", "pipeline", "sequence", "transport",
}

MCP_PREPHASE_TOOLING = [
    "get_scene_hierarchy",
    "find_game_objects",
    "get_gameobject_components",
    "get_component_inspector_properties",
    "find_component_usages",
]


def classify_prompt(prompt: str) -> str:
    q = prompt.lower()
    tokens = set(re.findall(r"[a-z0-9_]+", q))
    if any(term in tokens for term in SCENE_QUERY_TERMS):
        return QUERY_CLASS_SCENE_INTEGRATION
    if any(term in tokens for term in STRUCTURAL_QUERY_TERMS):
        return QUERY_CLASS_STRUCTURAL
    if any(term in tokens for term in OWNERSHIP_QUERY_TERMS):
        return QUERY_CLASS_OWNERSHIP
    if any(term in tokens for term in BEHAVIOR_QUERY_TERMS):
        return QUERY_CLASS_BEHAVIOR
    return QUERY_CLASS_OWNERSHIP


def preferred_sources_for_query_class(query_class: str, has_scene_context: bool) -> list[str]:
    if query_class == QUERY_CLASS_SCENE_INTEGRATION:
        base = [
            "gladekit_mcp_scene_hierarchy",
            "gladekit_mcp_component_inspection",
            "scene_audit_overlay",
            "runtime_code_records",
        ]
        if not has_scene_context:
            base.append("pass_scene_path_for_overlay")
        return base
    if query_class == QUERY_CLASS_STRUCTURAL:
        return ["structural_records", "assembly_records", "relation_records", "file_overview_records"]
    if query_class == QUERY_CLASS_BEHAVIOR:
        return ["runtime_member_records", "runtime_type_records", "relation_records", "file_overview_records"]
    return ["file_overview_records", "runtime_type_records", "runtime_member_records", "assembly_records"]


def query_workflow(prompt: str, has_scene_context: bool) -> dict[str, Any]:
    query_class = classify_prompt(prompt)
    return {
        "prompt": prompt,
        "query_class": query_class,
        "preferred_sources": preferred_sources_for_query_class(query_class, has_scene_context),
    }


def build_workflow_plan(
    prompts: list[str],
    scenario: str | None,
    scene_path: str | None,
    scene_report: dict[str, Any] | None,
) -> dict[str, Any]:
    query_plans = [query_workflow(prompt, scene_path is not None or bool(scene_report)) for prompt in prompts]
    live_scene_required = bool(scene_path) or any(plan["query_class"] == QUERY_CLASS_SCENE_INTEGRATION for plan in query_plans)
    scene_targets = [
        hotspot["game_object"]
        for hotspot in (scene_report or {}).get("hotspots", [])
        if hotspot.get("game_object")
    ]
    rationale = (
        "Use GladeKit MCP first for live Unity scene/component truth, then use codebase artifacts and retrieval "
        "for structural ownership and implementation details."
    )
    if scenario == "localai-llmunity":
        rationale += " This is especially important for LocalAI/LLMUnity transport, remote flags, and Qdrant/Cognee wiring."
    return {
        "pre_phase": {
            "strategy": "gladekit_mcp_first",
            "rationale": rationale,
            "live_scene_required": live_scene_required,
            "mcp_tools": MCP_PREPHASE_TOOLING,
            "scene_targets": scene_targets,
        },
        "queries": query_plans,
    }
