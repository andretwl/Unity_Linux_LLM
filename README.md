# Unity_Linux_LLM

Current-state documentation for the Unity NPC dialogue prototype in this repository.

## Overview

This project is a Unity `6000.5.1f1` prototype centered on `Assets/Scenes/NPCDialoguePrototype.unity`. It combines:

- direct LocalAI chat requests from `NPCDialogueManager`
- local LLMUnity `.rag` retrieval with a MiniLM embedding model
- optional Qdrant retrieval and a currently-disabled Cognee runtime layer
- per-NPC profiles and knowledge files
- editor tooling for NPC generation and scene/template workflows

The repo is not a pure LLMUnity sample and not a fully remote-only architecture. It is a hybrid prototype with several integration surfaces present, but only some enabled in the current scene.

## Current scene architecture

Main active objects and roles in `NPCDialoguePrototype`:

- `NPCDialogueSystem`
  - `NPCDialogueManager`
  - `NPCDialogueBootstrapper`
  - `NPCDialogueSmokeValidator`
  - `QdrantRAGService`
  - `NPCDialogueActionPlanner`
- `LLM`
  - shared chat-model `LLMUnity.LLM`
  - currently disabled in scene
  - configured local, not remote
- `LLMAgent`
  - shared `LLMUnity.LLMAgent`
  - currently disabled in scene
  - configured local, not remote
- `LLMRAG`
  - embedding-only `LLMUnity.LLM`
  - model: `all-MiniLM-L12-v2.Q4_K_M.gguf`
- `RAG`
  - local retrieval stack used by the runtime manager
- `CogneeMemoryService`
  - separate GameObject
  - endpoint: `http://localhost:8000/api/v1`
- `FunctionCalling`
  - present but inactive
  - uses `FunctionCallingAgent`
- `FunctionCallingAgent`
  - remote enabled
  - host `localhost`, port `8080`
  - currently references the shared `LLM`
- `FunctionCallingLLM`
  - separate remote-enabled `LLM`
  - present in scene, but not currently the `FunctionCallingAgent`'s assigned `llm`
- `NPCFlowLogger`
  - scene logger for structured JSONL event output

## Active manager configuration

The current `NPCDialogueManager` scene configuration is:

- `useRemoteServer = true`
- `remoteHost = localhost`
- `remotePort = 8080`
- `remoteModel = llama-3.1-8b-q4-k-m`
- `ragEmbeddingPath = RAG/NPCDialogues-minilm-chunked.rag`
- `useQdrantRag = false`
- `useCogneeMemory = false`
- `forceRemoteEmbedder = false`

This means the project currently runs with:

- direct HTTP chat requests to LocalAI for dialogue
- local `.rag` retrieval enabled
- Qdrant support available in scene but disabled
- Cognee support available in scene but disabled

## Repository layout

Key folders:

- `Assets/Scripts/Runtime/`
  - core runtime dialogue, retrieval, UI, history, and logging code
- `Assets/Scripts/Editor/`
  - NPC factory, scene utilities, and Glade-related editor tools
- `Assets/Scripts/Tests/Editor/`
  - editor-side tests, currently focused on flow logging
- `Assets/Data/NPCProfiles/`
  - current NPC profile assets: Butler, Maid, Chef
- `Assets/StreamingAssets/NPCs/`
  - NPC knowledge markdown and `profile.json` files
- `Assets/StreamingAssets/RAG/`
  - local `.rag` artifacts and metadata guidance
- `Tools/CodebaseEmbedder/`
  - offline-first project indexer and scene-aware audit tooling

## Runtime systems

### Dialogue manager

`Assets/Scripts/Runtime/NPCDialogueManager.cs`

- initializes scene references
- indexes NPC profiles
- loads and repairs saved histories
- builds or loads local `.rag` state
- plans lightweight gameplay actions from player input
- optionally queries Qdrant and Cognee
- sends direct HTTP requests to LocalAI when remote mode is enabled

### Local RAG

Core files:

- `Assets/Scripts/Runtime/NPCDialogue/NPCRAGImporter.cs`
- `Assets/Scripts/Runtime/NPCDialogue/QdrantRAGService.cs`
- `Assets/StreamingAssets/RAG/README.md`

The local `.rag` path in scene currently points at `NPCDialogues-minilm-chunked.rag`. The embedding model is the local `LLMRAG` object using `all-MiniLM-L12-v2.Q4_K_M.gguf`.

### Qdrant

`QdrantRAGService` is present and wired to the scene, but disabled by manager config. The current scene value for `collectionName` is `unity_linux_llm_codebase_v1`.

That collection name is part of the actual current state, but it should not be assumed to be the intended long-term NPC-memory collection.

### Cognee

`CogneeMemoryService` exists as a separate scene object and editor tooling can call into it, but `NPCDialogueManager.useCogneeMemory` is currently off.

Practical current policy:
- Hermes CLI agent memory may continue using the Cognee plugin/backend
- Unity runtime Cognee integration is not part of the active preferred lane right now
- codebase retrieval should continue to use Qdrant / CodebaseEmbedder, not Cognee

### Logging and history

Core files:

- `Assets/Scripts/Runtime/NPCDialogue/Logging/NPCFlowLogger.cs`
- `Assets/Scripts/Runtime/NPCDialogue/NPCHistoryStore.cs`

Behavior:

- logs structured JSONL events
- stores logs under `Application.persistentDataPath/NPCDialogue/Logs` by default
- stores per-NPC history under `Application.persistentDataPath/NPCDialogue/*.json`
- normalizes malformed saved histories into alternating `user`/`assistant` turns

## Current NPC content

Shipped profiles:

- Butler
- Maid
- Chef

Profile assets live in `Assets/Data/NPCProfiles/` and their knowledge lives in `Assets/StreamingAssets/NPCs/<slug>/knowledge.md`.

Each profile also carries:

- speaking and behavior controls
- action permissions
- sampling settings
- LoRA adapter path
- history save path

## Editor tooling

Current editor-side surfaces include:

- `NPCFactoryWindow`
- `CreateNPCTool`
- `MysterySceneTemplateGenerator`
- `HeadlessPlayMode`
- `TestQdrantRAG`

These tools generate NPC assets, write StreamingAssets knowledge, test chat flows, and integrate with Glade Agentic AI editor workflows.

## Assemblies

Defined assemblies:

- `NPCSystem.Runtime`
- `NPCSystem.Editor`
- `NPCSystem.Tests`

Runtime currently references:

- `undream.llmunity.Runtime`
- `GladeAgenticAI.Core`
- `Unity.InputSystem`
- `Unity.Nuget.Newtonsoft-Json`
- `Cloud.Unum.USearch`

Editor references `GladeKit.Bridge`; runtime does not.

## CodebaseEmbedder

Useful commands:

```bash
cd Tools/CodebaseEmbedder
env UV_CACHE_DIR=/tmp/uv-cache UV_TOOL_DIR=/tmp/uv-tools uv run codebase-embedder status --root ../..
env UV_CACHE_DIR=/tmp/uv-cache UV_TOOL_DIR=/tmp/uv-tools uv run codebase-embedder query --root ../.. --local "NPC dialogue architecture"
env UV_CACHE_DIR=/tmp/uv-cache UV_TOOL_DIR=/tmp/uv-tools uv run codebase-embedder audit --root ../.. --scene Assets/Scenes/NPCDialoguePrototype.unity --scenario localai-llmunity --local
```

If `uv` tries to use a read-only cache path in your session, keep the `/tmp` overrides.

## Retrieval index state

The project now has a structural retrieval experiment loop in place for Unity code queries:

- Stable collection: `unity_linux_llm_codebase_v1`
- Structural experiment collection: `unity_linux_llm_codebase_structural_v1`
- Hierarchy experiment collection: `unity_linux_llm_codebase_hierarchy_v1`
- Current smoke-query result: the structural collection ties the stable collection on the default matrix, and the hierarchy collection improves some namespace-summary prompts but still loses overall on the default matrix
- The indexer now emits higher-signal structural records such as `namespace`, `using_directive`, `file_overview`, `assembly`, and selected `relation` records
- The hierarchy profile also emits `namespace_summary` records for namespace-level aggregation
- Comparison reports are written to `.codebase-index/collection-comparison-*.md`
- The optimizer skill lives at `.agents/skills/unity-codebase-rag-optimizer/`

## Testing

Unity-side tests currently present:

- `Assets/Scripts/Tests/Editor/NPCFlowLoggerTests.cs`

Embedder tests:

```bash
cd Tools/CodebaseEmbedder
env UV_CACHE_DIR=/tmp/uv-cache UV_TOOL_DIR=/tmp/uv-tools uv run --extra test pytest -q
```

## Documentation note

This README is intended to describe the repo and scene as currently wired, not an aspirational target architecture. If you change scene references, manager toggles, or assembly boundaries, update this file and `AGENTS.md` in the same change.
