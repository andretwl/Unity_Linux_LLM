# Unity Codebase Embedder

Offline-first developer tooling for indexing this Unity project into Qdrant.

## Commands

```bash
cd Tools/CodebaseEmbedder
uv run --extra test pytest -q
uv run codebase-embedder status --root ../..
uv run codebase-embedder scan --root ../..
uv run codebase-embedder index --root ../.. --no-qdrant
uv run codebase-embedder index --root ../.. --reuse-artifacts --use-vector-cache --timings-json ../../.codebase-index/benchmarks/index-cache.json
uv run codebase-embedder query --root ../.. --local "where is qdrant rag search implemented"
uv run codebase-embedder audit --root ../.. --script Assets/Scripts/Runtime/NPCDialogue/QdrantRAGService.cs --scenario localai-llmunity
uv run codebase-embedder audit --root ../.. --scene Assets/Scenes/NPCDialoguePrototype.unity --scenario localai-llmunity --local
uv run codebase-embedder unity-validate --root ../..
```

The scanner writes artifacts under `.codebase-index/` at the Unity project root:

- `manifest.json`
- `asmdefs.json`
- `symbols.jsonl`
- `relations.jsonl`
- `chunks.jsonl`
- `index-report.md`

## Design

The v1 indexer scans filesystem assets, asmdef files, package metadata, selected docs, and C# syntax. It does not require Unity to be open. GladeKit MCP is optional for validation/enrichment.

The `audit` command is the best way to evaluate retrieval quality for holistic Unity decisions. It combines:

- script-level structural audit (namespaces, asmdef ownership, symbol counts, relation counts)
- smoke-query retrieval checks
- scenario presets such as `localai-llmunity` to inspect how well runtime/backend scripts outrank editor/docs for LocalAI + LLMUnity tasks
- optional scene-aware overlay via `--scene Assets/...unity` that parses the Unity scene YAML and reports LocalAI transport wiring, dedicated-agent splits, and Qdrant/Cognee/FunctionCalling hotspots in one report
- an explicit workflow section that classifies each prompt and recommends a GladeKit MCP pre-phase before repo-only retrieval when scene/component truth matters

## Recommended Workflow

For project questions that touch live Unity state, do not start from repo text alone.

1. Use GladeKit MCP first for live Unity truth.
   - `get_scene_hierarchy`
   - `find_game_objects`
   - `get_gameobject_components`
   - `get_component_inspector_properties`
   - `find_component_usages`
2. Use `codebase-embedder audit` to correlate that live state with runtime/editor ownership in indexed code.
3. Use `codebase-embedder query` to verify the same prompt classification and preferred-source guidance on individual questions.
4. Only then tune retrieval, chunking, prompts, or collection strategy.

This matters most for:

- scene wiring questions
- component enablement / remote flag questions
- LLMAgent / LLM / LocalAI transport questions
- Qdrant / Cognee integration ownership
- inspector-state mismatches between intended and actual runtime setup

The `query` command now prints a short workflow header before ranked results, and `--json` includes:

- `workflow.query_class`
- `workflow.preferred_sources`
- `results`

Qdrant collection: `unity_linux_llm_codebase_v1`.

## Efficiency Benchmarking

The CLI supports machine-readable timing reports for `status`, `scan`, `index`, `query`, and `audit`:

```bash
cd Tools/CodebaseEmbedder
env UV_CACHE_DIR=/tmp/uv-cache UV_TOOL_DIR=/tmp/uv-tools \
  uv run codebase-embedder scan --root ../.. --profile runtime \
  --timings-json ../../.codebase-index/benchmarks/scan-runtime.json
```

For faster repeated indexing during development, build artifacts once and reuse cached vectors for unchanged records:

```bash
cd Tools/CodebaseEmbedder
env UV_CACHE_DIR=/tmp/uv-cache UV_TOOL_DIR=/tmp/uv-tools \
  uv run codebase-embedder index --root ../.. --profile runtime \
  --reuse-artifacts --use-vector-cache --batch-size 64 \
  --collection unity_linux_llm_codebase_efficiency_v1 \
  --timings-json ../../.codebase-index/benchmarks/index-cache.json
```

LocalAI benchmark helpers:

```bash
python3 scripts/benchmark_localai_backends.py \
  --base-url http://127.0.0.1:8080/v1 \
  --embedding-model nomic-embed-text-v1.5 \
  --skip-chat \
  --out ../../.codebase-index/benchmarks/localai-embedding-smoke.jsonl

python3 scripts/benchmark_embeddings.py \
  --chunks ../../.codebase-index/chunks.jsonl \
  --model nomic-embed-text-v1.5 \
  --batch-sizes 1,4,8,16,32,64 \
  --out ../../.codebase-index/benchmarks/embedding-throughput.json
```
