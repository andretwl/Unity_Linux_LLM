from pathlib import Path

from codebase_embedder.config import CodebaseEmbedderConfig
from codebase_embedder.discovery import classify_unity_region, discover_project_files


def test_discovery_excludes_unity_generated_dirs(tmp_path: Path):
    (tmp_path / "Assets/Scripts/Runtime").mkdir(parents=True)
    (tmp_path / "Assets/Scripts/Runtime/Foo.cs").write_text("class Foo {}")
    (tmp_path / "Assets/Scripts/Runtime/Foo.asmdef").write_text('{"name":"Foo"}')
    (tmp_path / "Library/PackageCache/pkg").mkdir(parents=True)
    (tmp_path / "Library/PackageCache/pkg/Noise.cs").write_text("class Noise {}")
    (tmp_path / "Temp").mkdir()
    (tmp_path / "Temp/Noise.cs").write_text("class Noise {}")

    files = discover_project_files(CodebaseEmbedderConfig(project_root=tmp_path))

    assert [p.relative_to(tmp_path).as_posix() for p in files.csharp] == ["Assets/Scripts/Runtime/Foo.cs"]
    assert [p.relative_to(tmp_path).as_posix() for p in files.asmdefs] == ["Assets/Scripts/Runtime/Foo.asmdef"]


def test_classify_unity_region():
    assert classify_unity_region(Path("Assets/Scripts/Runtime/Foo.cs")) == "Runtime"
    assert classify_unity_region(Path("Assets/Scripts/Editor/Foo.cs")) == "Editor"
    assert classify_unity_region(Path("Assets/Scripts/Tests/Editor/Foo.cs")) == "Tests"
    assert classify_unity_region(Path("Assets/LLMUnity/Samples/RAG/Foo.cs")) == "Samples"
    assert classify_unity_region(Path("Assets/LLMUnity/Scripts/CogneeMemoryService.cs")) == "Runtime"
