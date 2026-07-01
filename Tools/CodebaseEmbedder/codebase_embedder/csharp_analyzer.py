from __future__ import annotations

import re
from pathlib import Path

from .asmdef_parser import AssemblyRecord, resolve_asmdef_for_file
from .discovery import classify_unity_region
from .records import IndexRecord, RelationRecord

NS_RE = re.compile(r"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)")
TYPE_RE = re.compile(r"(?:^|[;{}])\s*(?P<attrs>(?:\[[^\]]+\]\s*)*)(?P<mods>(?:(?:public|internal|private|protected|static|abstract|sealed|partial|readonly|unsafe|new)\s+)*)?(?P<kind>class|struct|interface|enum)\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s*:\s*(?P<bases>[^\{]+))?", re.MULTILINE)
METHOD_RE = re.compile(r"(?:^|[;{}])\s*(?P<attrs>(?:\[[^\]]+\]\s*)*)(?P<sig>(?:public|private|protected|internal|static|async|virtual|override|sealed|partial|extern|new)\s+[A-Za-z0-9_<>,\\.\?\[\]\s]+\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*\([^;{}]*\))", re.MULTILINE)
FIELD_RE = re.compile(r"(?:^|[;{}])\s*(?P<attrs>(?:\[[^\]]+\]\s*)*)(?P<mods>public|private|protected|internal)(?P<rest>[^;{}()=]+)\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=[^;]*)?;", re.MULTILINE)
CALL_RE = re.compile(r"\b([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)\s*\(")

USING_RE = re.compile(r"^\s*using\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;", re.MULTILINE)
SUMMARY_RE = re.compile(r"///\s*<summary>\s*(.*?)\s*</summary>", re.DOTALL)

_PURPOSE_KW = {
    "Client": "LLM client: handles remote/local LLM requests, completions, tokenization, and embedding tasks over the LocalAI backend",
    "Service": "background service: provides runtime functionality for NPC dialogue systems, LLM integration, and remote backend requests",
    "Manager": "manager: coordinates NPC dialogue, LLM backend configuration, remote server host/port, Qdrant RAG, and Cognee memory",
    "Agent": "LLM agent: dialogue selector/session agent that routes LLM requests and manages function-calling transport to the remote backend",
    "Setup": "setup: configures LLMUnity connection, backend LocalAI URLs, local model paths, and remote server parameters",
    "Bootstrapper": "bootstrap: initializes NPC dialogue system, auto-selects default NPC, and prewarms LLM and RAG connections",
    "Validator": "validator: smoke-tests NPC dialogue system health, verifies LLM connectivity, RAG backend, and integration checks",
    "RAG": "RAG retrieval: searches vector memory in Qdrant for context-relevant NPC knowledge embeddings and semantic queries",
    "Qdrant": "Qdrant RAG: manages Qdrant vector collections for NPC knowledge, embeddings search, semantic memory, and RAG queries",
    "Cognee": "Cognee memory: syncs and retrieves long-term NPC dialogue memory via Cognee knowledge graph and RAG embeddings",
}


def _line_at(text: str, index: int) -> int:
    return text.count("\n", 0, index) + 1


def _attrs(text: str) -> list[str]:
    names: list[str] = []
    for match in re.finditer(r"\[\s*([A-Za-z_][A-Za-z0-9_.]*)", text or ""):
        names.append(match.group(1).split(".")[-1])
    return names


def _namespace_at(text: str, index: int) -> str:
    matches = [m for m in NS_RE.finditer(text) if m.start() <= index]
    return matches[-1].group(1) if matches else ""


def _bases(raw: str | None) -> tuple[list[str], list[str]]:
    if not raw:
        return [], []
    vals = [v.strip().split("<", 1)[0].strip() for v in raw.split(",") if v.strip()]
    interfaces = [v for v in vals if v.startswith("I") and len(v) > 1 and v[1].isupper()]
    bases = [v for v in vals if v not in interfaces]
    return bases, interfaces


def _method_end_line(text: str, start: int) -> int:
    brace = text.find("{", start)
    if brace == -1:
        return _line_at(text, start)
    depth = 0
    for i in range(brace, len(text)):
        ch = text[i]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return _line_at(text, i)
    return _line_at(text, len(text))


def _extract_summary(text: str, type_start: int, type_name: str) -> str:
    """Extract the XML doc /// <summary> block that precedes the type declaration."""
    preceding = text[:type_start]
    matches = list(SUMMARY_RE.finditer(preceding))
    if not matches:
        return ""
    # Take the closest summary before the type
    closest = matches[-1]
    summary = closest.group(1)
    summary = re.sub(r"\s+", " ", summary).strip()
    return summary


def _purpose_text(type_name: str, ns: str, region: str, asm_name: str) -> str:
    """Synthesise a short contextual-purpose line from naming patterns."""
    for keyword, purpose in _PURPOSE_KW.items():
        if keyword.lower() in type_name.lower():
            return purpose
    if "Runtime" in region:
        return f"Runtime {region.lower()} code in {asm_name}: {type_name}"
    return f"{region} {type_name} in assembly {asm_name}"


def analyze_csharp_files(root: Path, csharp_paths: list[Path], assemblies: list[AssemblyRecord], project: str = "Unity_Linux_LLM") -> tuple[list[IndexRecord], list[RelationRecord]]:
    records: list[IndexRecord] = []
    relations: list[RelationRecord] = []
    for path in csharp_paths:
        text = path.read_text(encoding="utf-8", errors="ignore")
        rel = path.relative_to(root).as_posix() if path.is_absolute() or str(path).startswith(str(root)) else path.as_posix()
        asm = resolve_asmdef_for_file(path, assemblies)
        asm_name = asm.name if asm else ""
        root_ns = asm.root_namespace if asm else ""
        region = classify_unity_region(Path(rel))
        usings = USING_RE.findall(text)
        declared_namespaces = sorted(set(NS_RE.findall(text)))

        # --- first pass: collect every type position, type-name, and method names per type ---
        type_positions: list[tuple[int, str, str, str, str, str, list[str], list[str]]] = []
        # each: (start_index, name, kind, ns, bases_str, attrs_str, base_list, interface_list)

        for m in TYPE_RE.finditer(text):
            kind = m.group("kind")
            name = m.group("name")
            ns = _namespace_at(text, m.start()) or root_ns
            base_str = m.group("bases") or ""
            bases_clean, interfaces = _bases(base_str)
            type_positions.append((
                m.start(), name, kind, ns, base_str.strip(),
                m.group("attrs") or "",
                bases_clean, interfaces,
            ))

        types_by_namespace: dict[str, list[str]] = {}
        for _start, type_name, _kind, ns, _base_str, _attrs_str, _bases_clean, _interfaces in type_positions:
            if not ns:
                continue
            types_by_namespace.setdefault(ns, []).append(type_name)

        # Build a set of method names per type by scanning the METHOD_RE results
        # and assigning each to the nearest preceding type.
        type_methods: dict[int, list[str]] = {pos[0]: [] for pos in type_positions}
        # Also collect ALL member names for the member-record loop below
        all_method_names: set[str] = set()
        method_matches: list[tuple[int, re.Match]] = []  # (start_of_type_index, method_match)

        for mm in METHOD_RE.finditer(text):
            name = mm.group("name")
            if name in {"if", "for", "foreach", "while", "switch", "catch", "using", "lock"}:
                continue
            all_method_names.add(name)
            # find the innermost (last) type that starts before this method
            best_type_start = max(
                (ts for ts, *_rest in type_positions if ts < mm.start()),
                default=None,
            )
            if best_type_start is not None and best_type_start in type_methods:
                type_methods[best_type_start].append(name)
            method_matches.append((best_type_start or 0, mm))

        if declared_namespaces or usings or type_positions:
            type_names = sorted({name for _start, name, *_rest in type_positions})
            member_names = sorted(all_method_names)
            file_payload = {
                "project": project,
                "path": rel,
                "relative_dir": str(Path(rel).parent),
                "unity_region": region,
                "asmdef": asm_name,
                "asmdef_path": asm.path if asm else "",
                "root_namespace": root_ns,
                "declared_namespaces": declared_namespaces,
                "using_directives": sorted(set(usings)),
                "type_names": type_names,
                "member_names": member_names,
                "line_start": 1,
                "line_end": max(1, text.count("\n") + 1),
            }
            file_lines = [
                f"File overview {rel}",
                f"Assembly {asm_name or '-'}",
                f"Region {region}",
                f"Namespaces: {', '.join(declared_namespaces) or '-'}",
                f"Using directives: {', '.join(sorted(set(usings))) or '-'}",
                f"Types: {', '.join(type_names) or '-'}",
                f"Members: {', '.join(member_names) or '-'}",
            ]
            records.append(IndexRecord("file_overview", f"file:{rel}", "\n".join(file_lines), file_payload))

        for ns_match in NS_RE.finditer(text):
            ns = ns_match.group(1)
            namespace_payload = {
                "project": project,
                "path": rel,
                "relative_dir": str(Path(rel).parent),
                "unity_region": region,
                "asmdef": asm_name,
                "asmdef_path": asm.path if asm else "",
                "root_namespace": root_ns,
                "namespace": ns,
                "declared_type_names": sorted(set(types_by_namespace.get(ns, []))),
                "using_directives": sorted(set(usings)),
                "symbol_kind": "namespace",
                "line_start": _line_at(text, ns_match.start()),
                "line_end": _line_at(text, ns_match.end()),
            }
            namespace_lines = [
                f"Namespace {ns}",
                f"Path {rel}",
                f"Assembly {asm_name or '-'}",
                f"Region {region}",
                f"Declared types: {', '.join(namespace_payload['declared_type_names']) or '-'}",
                f"Using directives: {', '.join(namespace_payload['using_directives']) or '-'}",
            ]
            records.append(IndexRecord("namespace", f"namespace:{ns}:{rel}:{namespace_payload['line_start']}", "\n".join(namespace_lines), namespace_payload))

        for using_match in USING_RE.finditer(text):
            using_ns = using_match.group(1)
            using_payload = {
                "project": project,
                "path": rel,
                "relative_dir": str(Path(rel).parent),
                "unity_region": region,
                "asmdef": asm_name,
                "asmdef_path": asm.path if asm else "",
                "root_namespace": root_ns,
                "using_namespace": using_ns,
                "declared_namespaces": declared_namespaces,
                "type_names": sorted({name for _start, name, *_rest in type_positions}),
                "symbol_kind": "using_directive",
                "line_start": _line_at(text, using_match.start()),
                "line_end": _line_at(text, using_match.end()),
            }
            using_lines = [
                f"Using directive {using_ns}",
                f"Path {rel}",
                f"Assembly {asm_name or '-'}",
                f"Declared namespaces: {', '.join(declared_namespaces) or '-'}",
                f"Types in file: {', '.join(using_payload['type_names']) or '-'}",
            ]
            records.append(IndexRecord("using_directive", f"using:{using_ns}:{rel}:{using_payload['line_start']}", "\n".join(using_lines), using_payload))
            for declared_ns in declared_namespaces:
                relations.append(RelationRecord("namespace-uses-namespace", declared_ns, using_ns, rel, {"asmdef": asm_name}))

        # --- create enriched type records ---
        type_name_by_start: dict[int, str] = {}
        type_ns_by_start: dict[int, str] = {}
        for (start_idx, tname, kind, ns, base_str, attrs_str, bases_clean, interfaces) in type_positions:
            type_name_by_start[start_idx] = tname
            type_ns_by_start[start_idx] = ns
            summary = _extract_summary(text, start_idx, tname)
            method_names = type_methods.get(start_idx, [])
            purpose = _purpose_text(tname, ns, region, asm_name)

            line_start = _line_at(text, start_idx)
            payload = {
                "project": project, "path": rel, "relative_dir": str(Path(rel).parent), "unity_region": region,
                "asmdef": asm_name, "asmdef_path": asm.path if asm else "", "root_namespace": root_ns,
                "namespace": ns, "type_name": tname, "symbol_kind": kind, "line_start": line_start,
                "line_end": _method_end_line(text, start_idx), "attributes": _attrs(attrs_str),
                "base_types": bases_clean, "interfaces": interfaces, "using_directives": usings,
            }
            signature = f"{kind} {tname}" + (f" : {base_str}" if base_str else "")

            # Build enriched text
            text_parts = [signature]
            text_parts.append(f"Assembly {asm_name}")
            text_parts.append(f"Namespace {ns}")
            text_parts.append(f"Path {rel}")
            text_parts.append(f"Region {region}")
            text_parts.append(f"Purpose: {purpose}")
            if summary:
                text_parts.append(f"Summary: {summary}")
            if method_names:
                text_parts.append(f"Methods: {', '.join(sorted(set(method_names)))}")
            text_parts.append(f"Base types: {', '.join(bases_clean) if bases_clean else '-'}")
            text_parts.append(f"Interfaces: {', '.join(interfaces) if interfaces else '-'}")

            records.append(IndexRecord("type", f"type:{ns}.{tname}:{rel}", "\n".join(text_parts), payload))
            if ns:
                relations.append(RelationRecord("namespace-contains-type", ns, f"{ns}.{tname}", rel, {"asmdef": asm_name}))
            for base in bases_clean:
                relations.append(RelationRecord("inherits", f"{ns}.{tname}", base, rel, {"asmdef": asm_name}))
            for iface in interfaces:
                relations.append(RelationRecord("implements", f"{ns}.{tname}", iface, rel, {"asmdef": asm_name}))

        if not type_positions:
            continue  # skip files with no type declarations

        # --- second pass: method member records ---
        for (best_type_start, mm) in method_matches:
            name = mm.group("name")
            if name in all_method_names:
                tname = type_name_by_start.get(best_type_start, Path(path).stem)
                ns = type_ns_by_start.get(best_type_start, root_ns)
            else:
                tname = Path(path).stem
                ns = root_ns

            attrs = _attrs(mm.group("attrs"))
            line_start = _line_at(text, mm.start())
            line_end = _method_end_line(text, mm.start())
            sig = " ".join(mm.group("sig").split())
            payload = {"project": project, "path": rel, "relative_dir": str(Path(rel).parent), "unity_region": region,
                       "asmdef": asm_name, "namespace": ns, "type_name": tname, "member_name": name,
                       "symbol_kind": "method", "signature": sig, "line_start": line_start, "line_end": line_end,
                       "attributes": attrs}
            fq = f"{ns}.{tname}.{name}" if ns else f"{tname}.{name}"
            records.append(IndexRecord("member", f"member:{fq}:{line_start}:{rel}", f"{sig}\n{rel}:{line_start}-{line_end}\nType {tname} in {ns}", payload))
            relations.append(RelationRecord("type-contains-member", f"{ns}.{tname}", fq, rel, {"asmdef": asm_name}))

        # --- third pass: serialized field records ---
        current_type = type_name_by_start.get(0, Path(path).stem) if type_positions else Path(path).stem
        current_ns = type_ns_by_start.get(0, root_ns) if type_positions else root_ns
        for m in FIELD_RE.finditer(text):
            attrs = _attrs(m.group("attrs"))
            is_serialized = "SerializeField" in attrs or m.group("mods") == "public"
            if not is_serialized:
                continue
            name = m.group("name")
            line = _line_at(text, m.start())
            # find containing type
            containing_start = max(
                (ts for ts, *_rest in type_positions if ts < m.start()),
                default=None,
            )
            if containing_start is not None:
                tname = type_name_by_start.get(containing_start, current_type)
                ns = type_ns_by_start.get(containing_start, current_ns)
            else:
                tname = current_type
                ns = current_ns
            payload = {"project": project, "path": rel, "relative_dir": str(Path(rel).parent), "unity_region": region,
                       "asmdef": asm_name, "namespace": ns, "type_name": tname, "member_name": name,
                       "symbol_kind": "field", "line_start": line, "line_end": line, "attributes": attrs,
                       "signature": " ".join((m.group("mods") + m.group("rest") + " " + name).split())}
            records.append(IndexRecord("serialized_field", f"serialized_field:{ns}.{tname}.{name}:{rel}", f"Serialized field {tname}.{name} in {rel}\nType {tname}, Namespace {ns}", payload))

        # --- fourth pass: call relations ---
        for call in CALL_RE.finditer(text):
            target = call.group(1)
            if target in {"if", "for", "foreach", "while", "switch", "catch", "using", "nameof", "typeof", "new"}:
                continue
            # find containing type
            containing_start = max(
                (ts for ts, *_rest in type_positions if ts < call.start()),
                default=None,
            )
            tname = type_name_by_start.get(containing_start, current_type) if containing_start is not None else current_type
            fq_caller = f"{type_ns_by_start.get(containing_start, current_ns)}.{tname}" if containing_start is not None else f"{current_ns}.{tname}"
            relations.append(RelationRecord("calls", fq_caller, target, rel, {"asmdef": asm_name, "line_start": _line_at(text, call.start())}))

    return records, relations
