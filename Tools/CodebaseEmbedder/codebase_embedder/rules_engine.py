"""Config-driven codebase rule checking.

Reads .codebaserules.yaml and applies each rule against the specified C# source files.
Supports three check types:
  - text: raw text match (presence/absence of a substring or multiline pattern)
  - regex: per-line regex match
  - roslyn: structural check that requires a full codebase scan (deferred)
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import yaml


@dataclass(slots=True)
class RuleFinding:
    rule_id: str
    title: str
    severity: str
    relative_path: str
    line: int
    message: str
    agents_md_ref: str = ""


@dataclass(slots=True)
class RuleDef:
    id: str
    title: str
    severity: str
    agents_md_ref: str
    check: str  # text | regex | roslyn
    pattern: str = ""
    message: str = ""
    description: str = ""
    inverse: bool = False
    exclude_files: list[str] = field(default_factory=list)
    auto_fix: str | None = None


def load_rules(config_path: Path) -> list[RuleDef]:
    if not config_path.exists():
        raise FileNotFoundError(f"Rules config not found at {config_path}")
    raw = yaml.safe_load(config_path.read_text(encoding="utf-8"))
    rules: list[RuleDef] = []
    for r in raw.get("rules", []):
        rules.append(RuleDef(
            id=r.get("id", ""),
            title=r.get("title", ""),
            severity=r.get("severity", "Info"),
            agents_md_ref=r.get("agents_md_ref", ""),
            check=r.get("check", "regex"),
            pattern=r.get("pattern", ""),
            message=r.get("message", ""),
            description=r.get("description", ""),
            inverse=r.get("inverse", False),
            exclude_files=r.get("exclude_files", []),
            auto_fix=r.get("auto_fix"),
        ))
    return rules


def matches_exclude(rel_path: str, patterns: list[str]) -> bool:
    """Check if a relative path matches any glob-style exclude pattern."""
    import fnmatch
    for pat in patterns:
        if fnmatch.fnmatch(rel_path, pat):
            return True
    return False


def check_file(rel_path: str, text: str, rule: RuleDef) -> list[RuleFinding]:
    """Run a single rule against a single file."""
    if matches_exclude(rel_path, rule.exclude_files):
        return []

    findings: list[RuleFinding] = []

    if rule.check == "text":
        # Text check: pattern present/absent throughout the file
        has_match = rule.pattern in text
        if rule.inverse:
            # inverse=true: flag if pattern is NOT present
            if not has_match:
                findings.append(RuleFinding(
                    rule_id=rule.id,
                    title=rule.title,
                    severity=rule.severity,
                    relative_path=rel_path,
                    line=text.count("\n") + (0 if text.endswith("\n") else 1),
                    message=rule.message or f"Expected pattern '{rule.pattern}' not found.",
                    agents_md_ref=rule.agents_md_ref,
                ))
        else:
            # flag if pattern IS present
            if has_match:
                findings.append(RuleFinding(
                    rule_id=rule.id,
                    title=rule.title,
                    severity=rule.severity,
                    relative_path=rel_path,
                    line=1,
                    message=rule.message or f"Found '{rule.pattern}' in file.",
                    agents_md_ref=rule.agents_md_ref,
                ))

    elif rule.check == "regex":
        # Per-line regex check
        lines = text.split("\n")
        compiled = re.compile(rule.pattern)
        for i, line_text in enumerate(lines, 1):
            for match in compiled.finditer(line_text):
                # Get the captured group or full match
                matched_text = match.group(0)
                msg = rule.message.replace("{match}", matched_text)
                findings.append(RuleFinding(
                    rule_id=rule.id,
                    title=rule.title,
                    severity=rule.severity,
                    relative_path=rel_path,
                    line=i,
                    message=msg,
                    agents_md_ref=rule.agents_md_ref,
                ))

    elif rule.check == "roslyn":
        # Structural checks deferred to scan output
        # We emit a single file-level note suggesting the full scan
        pass  # Handled separately in the report header

    return findings


def check_all_files(
    project_root: Path,
    rules: list[RuleDef],
    csharp_paths: list[Path],
    target_dir: str | None = None,
    exclude_patterns: list[str] | None = None,
) -> list[RuleFinding]:
    """Run all applicable rules against all C# files."""
    import fnmatch as _fnmatch
    all_findings: list[RuleFinding] = []
    roslyn_rules = [r for r in rules if r.check == "roslyn"]

    for path in csharp_paths:
        rel = path.relative_to(project_root).as_posix()
        if target_dir and not rel.startswith(target_dir):
            continue
        if exclude_patterns and any(_fnmatch.fnmatch(rel, pat) for pat in exclude_patterns):
            continue
        try:
            text = path.read_text(encoding="utf-8", errors="ignore")
        except Exception:
            continue

        for rule in rules:
            if rule.check == "roslyn":
                continue  # Skip roslyn rules in per-file check
            all_findings.extend(check_file(rel, text, rule))

    # Add roslyn rule notes once
    if roslyn_rules:
        all_findings.append(RuleFinding(
            rule_id="SCAN01",
            title="Structural rules require full scan",
            severity="Info",
            relative_path="(project root)",
            line=0,
            message=f"These structural rules require running 'codebase-embedder scan': "
                    f"{', '.join(f'{r.id} ({r.title})' for r in roslyn_rules)}. "
                    f"Run once, then review the symbol/relation artifacts in .codebase-index/.",
            agents_md_ref="",
        ))

    return all_findings


def format_check_report(
    findings: list[RuleFinding],
    project_root: Path,
    min_severity: str = "Suggestion",
) -> str:
    """Format findings as a markdown report."""
    severity_order = {"Error": 0, "Warning": 1, "Suggestion": 2, "Info": 3}
    min_level = severity_order.get(min_severity, 2)

    lines = [
        "# Codebase Rule Check Report",
        "",
        f"Generated: {__import__('datetime').datetime.now().isoformat()}",
        f"Project: {project_root}",
        "",
    ]

    filtered = [f for f in findings if severity_order.get(f.severity, 3) <= min_level]

    if not filtered:
        lines.append("**No findings.**")
        return "\n".join(lines) + "\n"

    # Summary
    by_sev: dict[str, int] = {}
    for f in filtered:
        by_sev[f.severity] = by_sev.get(f.severity, 0) + 1
    lines.append("## Summary")
    lines.append("")
    for sev in ["Error", "Warning", "Suggestion", "Info"]:
        if sev in by_sev:
            lines.append(f"- **{sev}**: {by_sev[sev]}")
    lines.append(f"- **Total**: {len(filtered)}")
    lines.append("")

    # Group by rule
    by_rule: dict[str, list[RuleFinding]] = {}
    for f in filtered:
        by_rule.setdefault(f"{f.rule_id}: {f.title}", []).append(f)

    for rule_key in sorted(by_rule.keys()):
        rule_findings = by_rule[rule_key]
        rule_id = rule_findings[0].rule_id
        sev = rule_findings[0].severity
        ref = rule_findings[0].agents_md_ref

        lines.append(f"## {rule_key}")
        lines.append("")
        lines.append(f"- Severity: **{sev}**")
        if ref:
            lines.append(f"- Reference: {ref}")
        lines.append(f"- Occurrences: {len(rule_findings)}")
        lines.append("")

        for f in rule_findings[:20]:  # Cap per rule
            loc = f"{f.relative_path}:{f.line}" if f.line > 0 else f.relative_path
            lines.append(f"  - `{loc}`: {f.message}")

        if len(rule_findings) > 20:
            lines.append(f"  - *... and {len(rule_findings) - 20} more occurrences*")
        lines.append("")

    return "\n".join(lines) + "\n"


def run_check(
    project_root: Path,
    rules_path: Path | None = None,
    target_dir: str | None = None,
    output_path: str | None = None,
    min_severity: str = "Suggestion",
    json_output: bool = False,
    exclude_patterns: list[str] | None = None,
) -> int:
    """Main entry point for the check command."""
    if rules_path is None:
        rules_path = project_root / ".codebaserules.yaml"

    rules = load_rules(rules_path)
    csharp_paths = sorted(project_root.rglob("*.cs"))

    # Filter out build artifacts
    exclude_dirs = {".git", ".venv", "Library", "Temp", "Logs", "obj", "bin", "Build", "Builds", "node_modules", "__pycache__", "Packages"}
    csharp_paths = [p for p in csharp_paths if not any(d in p.parts for d in exclude_dirs)]

    findings = check_all_files(project_root, rules, csharp_paths, target_dir, exclude_patterns)

    if json_output:
        output = json.dumps(
            [{"rule_id": f.rule_id, "severity": f.severity, "path": f.relative_path, "line": f.line, "message": f.message}
             for f in findings], indent=2)
    else:
        output = format_check_report(findings, project_root, min_severity)

    if output_path:
        Path(output_path).write_text(output, encoding="utf-8")
        print(f"Report written to {output_path}")

    print(output)

    # Exit code: non-zero if any Warnings or Errors
    error_count = sum(1 for f in findings if f.severity in ("Error", "Warning"))
    return 1 if error_count > 0 else 0
