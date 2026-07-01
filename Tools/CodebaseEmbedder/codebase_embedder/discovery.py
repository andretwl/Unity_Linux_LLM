from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from .config import CodebaseEmbedderConfig


@dataclass(slots=True)
class ProjectFiles:
    csharp: list[Path]
    asmdefs: list[Path]
    docs: list[Path]
    manifest: Path | None
    package_lock: Path | None


def is_excluded(path: Path, config: CodebaseEmbedderConfig) -> bool:
    parts = set(path.parts)
    if parts & config.exclude_dirs:
        return True
    if not config.include_samples and "Samples" in parts:
        return True
    if not config.include_package_cache and "PackageCache" in parts:
        return True
    return False


def classify_unity_region(path: Path) -> str:
    parts = path.as_posix().split("/")
    if "Tests" in parts:
        return "Tests"
    if "Editor" in parts:
        return "Editor"
    if "Scenes" in parts:
        return "Scene"
    if "Samples" in parts:
        return "Samples"
    if "Runtime" in parts:
        return "Runtime"
    if parts[:3] == ["Assets", "LLMUnity", "Scripts"]:
        return "Runtime"
    if parts and parts[0] == "Packages":
        return "Package"
    return "Unknown"


def _discover(root: Path, pattern: str, config: CodebaseEmbedderConfig) -> list[Path]:
    return sorted(p for p in root.rglob(pattern) if p.is_file() and not is_excluded(p.relative_to(root), config))


def discover_project_files(config: CodebaseEmbedderConfig) -> ProjectFiles:
    root = Path(config.project_root)
    # Unity-generated and package-manager folders can dwarf first-party code and
    # make agent queries noisy. v1 indexes project assets by default; embedded
    # packages under Assets/ (such as Assets/LLMUnity) are still included.
    assets_root = root / "Assets"
    csharp = _discover(assets_root, "*.cs", config) if assets_root.exists() else []
    asmdefs = _discover(assets_root, "*.asmdef", config) if assets_root.exists() else []
    docs = [p for p in _discover(root, "*.md", config) if _include_doc(p.relative_to(root))]
    if getattr(config, "include_scenes", False) and assets_root.exists():
        docs.extend(_discover(root, "*.unity", config))
    manifest = root / "Packages/manifest.json"
    lock = root / "Packages/packages-lock.json"
    return ProjectFiles(csharp, asmdefs, docs, manifest if manifest.exists() else None, lock if lock.exists() else None)


def _include_doc(rel: Path) -> bool:
    s = rel.as_posix()
    if any(name in s.lower() for name in ["license", "changelog", "third party notices", "code_of_conduct"]):
        return False
    return (
        s.startswith("Assets/MysteryTemplates/")
        or s.startswith("Assets/StreamingAssets/")
        or s.startswith("Assets/LLMUnity/Docs/")
        or s.endswith("README.md")
        or "/Documentation~/" in s
    )
