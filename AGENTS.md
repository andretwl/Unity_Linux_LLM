# Unity_Linux_LLM — Comprehensive Agent Reference

This document is the single source of truth for every LLM agent working on this project. It covers code conventions, project structure, backend services, scene wiring, testing, and embedder configuration.

---

## Quick Navigation

| Section | What You Need |
|---------|---------------|
| [1. Code Conventions](#1-code-conventions) | Naming, patterns, editorconfig, safety gates |
| [2. Project Structure](#2-project-structure) | Folders, asmdefs, namespace hierarchy |
| [3. Scene Wiring (Current)](#3-scene-wiring-current) | What's actually in the active scene |
| [4. Backend Services](#4-backend-services) | LocalAI, Supabase, Qdrant, Cognee |
| [5. Codebase Embedder](#5-codebase-embedder) | Qdrant indexing, querying, profiles |
| [6. Datadog Monitoring](#6-datadog-monitoring) | Metrics, log collection, dashboards |
| [7. Testing](#7-testing) | Test files, patterns, coverage |
| [8. Networking & Auth](#8-networking--auth) | Transport config, startup modes, auth flow |
| [9. Editor Tooling](#9-editor-tooling) | GladeKit MCP, NPCFactory, automation |
| [10. Safety Gates](#10-safety-gates) | What requires explicit approval |

---

## 1. Code Conventions

### 1.1 Naming Rules (enforced by `.editorconfig`)

| Scope | Convention | Example |
|-------|-----------|---------|
| Private fields | `_camelCase` prefix underscore | `_authService`, `_playerName` |
| Serialized private fields | `[SerializeField] private _camelCase` + `[FormerlySerializedAs]` | `[FormerlySerializedAs("authService")] [SerializeField] PlayerAuthService _authService;` |
| Public fields (avoid) | PascalCase | `TickRate`, `MaxRetries` |
| Public properties | PascalCase | `public PlayerAuthService AuthService { get; set; }` |
| Methods | PascalCase | `HandleAuthSuccess()`, `ProbeAsync()` |
| Local variables | `camelCase` | `playerCount`, `result` |
| Parameters | `camelCase` | `string username`, `int port` |
| Events | PascalCase | `onLoginSuccess`, `onHostStarted` |
| Constants | PascalCase | `DefaultPort`, `MaxRetries` |
| Namespaces | PascalCase, `NPCSystem.*` | `NPCSystem`, `NPCSystem.Tests` |

### 1.2 Phase 4 `[SerializeField] private` Pattern

Every public Inspector-exposed field has been converted. The required pattern:

```csharp
[FormerlySerializedAs("oldFieldName")]
[SerializeField]
private Type _camelCase;
public Type OldFieldName { get => _camelCase; set => _camelCase = value; }
```

**Rules:**
- ALWAYS add `[FormerlySerializedAs]` with the OLD PascalCase name so scene/prefab serialization isn't lost.
- Test accessors need a `set` on the public property (getter-only breaks test compilation).
- Public properties without setters are fine when only read-access is needed externally.
- Applied to all ~25 runtime MonoBehaviours across the codebase.

### 1.3 Async Pattern

```csharp
// Standard async try/finally with scope logging
try
{
    await Task.Yield();           // Yield on single-thread (WebGL-safe)
    await SomeApiCallAsync();
}
finally
{
    // cleanup always runs
}
```

**WebGL note:** `Task.Delay(1)` and `Task.Yield()` work in editor and on WebGL. Avoid `ConfigureAwait(false)` (Unity needs the main thread).

### 1.4 Formatting Rules

| Rule | Setting |
|------|---------|
| Indentation | Spaces, 4 per level |
| Braces | Allman (new line) — `csharp_new_line_before_open_brace = all` |
| Line endings | LF |
| Trailing whitespace | Trimmed |
| Final newline | Inserted |
| Max line length | Not enforced (practical ~120) |

### 1.5 XML Documentation

Public API surface should have `/// <summary>` and `/// <param>` / `/// <returns>` where applicable. Private methods may omit obvious docs. Do NOT leave redundant comments like `// Close the auth UI immediately` when `CloseAuthUI()` is self-documenting.

### 1.6 Key Anti-Pattern Rules (Phase 7)

- **No boolean flag parameters** on methods — split into named methods instead.
- **No hard-coded `"localhost"` strings** — use `NPCFlowLogger.IsLocalHost(host)` which checks both `"localhost"` and `"127.0.0.1"` case-insensitively.
- **No commented-out code** — delete it.
- **No TODO/FIXME/HACK** — address or remove.
- **No single-letter variables** outside `for` loop counters.

---

## 2. Project Structure

### 2.1 Top-Level Unity Folders

```
Assets/
├── Data/                          # ScriptableObject profile assets
│   └── NPCProfiles/               #   Butler, Maid, Chef .asset files
├── Scenes/
│   └── NPCDialoguePrototype1.unity # THE active scene (source of truth)
├── Scripts/
│   ├── Runtime/                   # Gameplay/network/dialogue code
│   │   ├── Initialization/        #   Bootstrapping, readiness service
│   │   ├── LocalRAG/              #   Local .rag file handling
│   │   ├── Networking/            #   NetworkBehaviour, transport, M2M
│   │   ├── NPCDialogue/           #   Dialogue manager, action planner, auth
│   │   │   └── Logging/           #     NPCFlowLogger
│   │   └── Supabase/              #     Supabase repository layer
│   ├── Editor/                    # Editor-only tooling
│   │   └── Tools/                 #     CreateNPCTool
│   └── Tests/Editor/              # Unit tests (23 files, 149 tests)
├── Prefabs/                       # Network prefabs (Player, NPC, Item)
├── Resources/                     # Runtime-loaded assets
├── StreamingAssets/               # Knowledge .md files, .rag files
│   └── NPCs/                      #   Per-NPC knowledge markdown
├── Shaders/                       # Custom shaders
├── Animations/                    # Animation controllers and clips
└── InputSystem_Actions.inputactions  # Input Action Asset
```

### 2.2 Assembly Definitions

| Asmdef | Path | Platform | References |
|--------|------|----------|------------|
| `NPCSystem.Runtime` | `Assets/Scripts/Runtime/NPCSystem.Runtime.asmdef` | All | `Unity.InputSystem`, `Unity.Netcode.Runtime`, `Unity.Collections`, `Unity.DedicatedServer.MultiplayerRoles`, `Unity.TextMeshPro`, `EditorAttributes` + precompiled: `Newtonsoft.Json`, `Supabase.*`, `System.Reactive`, `Websocket.Client` |
| `NPCSystem.Editor` | `Assets/Scripts/Editor/NPCSystem.Editor.asmdef` | Editor only | `NPCSystem.Runtime`, `UnityEditor.TestRunner`, `Unity.Settings.Editor` |
| `NPCSystem.Tests` | `Assets/Scripts/Tests/Editor/NPCSystem.Tests.asmdef` | Editor only (UNITY_INCLUDE_TESTS) | `NPCSystem.Runtime`, `GladeKit.Bridge`, `Unity.Netcode.Runtime`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner` + precompiled: `nunit.framework` |

**Discipline:**
- Runtime code never references Editor assemblies.
- Tests use direct instantiation (`new GameObject() + AddComponent<T>()`) — no scene dependency.
- `GladeKit.Bridge` is Editor-only (referenced only by Tests).

### 2.3 Namespace Hierarchy

```
NPCSystem                          # All runtime code
NPCSystem.Tests                    # All unit tests
NPCSystem.Editor                   # Editor tooling classes
```

**Note:** There is a COMPILATION WARNING for `NPCDialogueManager.SendMessage(string)` hiding `Component.SendMessage(string)` — planned rename in a controlled cleanup. Do NOT use the inherited `Component.SendMessage()`.

### 2.4 Dedicated Server Structure

```
Assets/:
  ├── Scripts/
  │   ├── Runtime/...
  │   ├── Editor/...
  │   └── Tests...
Builds/Server/                     # Server binary output
docker/
  ├── Dockerfile                   # ubuntu:22.04 server container image
  ├── docker-compose.yml           # network_mode: host, bind-mounts Builds/Server/
  └── entrypoint.sh                # --batchmode -npc-server flags
```

---

## 3. Scene Wiring (Current)

### 3.1 Active Scene

`Assets/Scenes/NPCDialoguePrototype1.unity` — the ONLY authoritative scene.

### 3.2 Key GameObjects

| GameObject | Key Components | Status |
|-----------|---------------|--------|
| `NPCDialogueSystem` | `NPCDialogueManager`, `NPCDialogueBootstrapper`, `QdrantRAGService`, `NPCDialogueActionPlanner` | Active |
| `LLM` | `LLMUnity.LLM` (`enabled=false`, `_remote=0`, port 13333, model `llama-3.1-8b-q4_k_m.gguf`) | Present but inactive (manager uses HTTP to LocalAI) |
| `LLMAgent` | `LLMUnity.LLMAgent` (`enabled=false`) | Present but inactive |
| `LLMRAG` | `LLMUnity.LLM` (`_embeddingsOnly=1`, model `all-MiniLM-L12-v2.Q4_K_M.gguf`) | Active for local `.rag` embedding |
| `RAG` | LLMUnity RAG component | Assigned to `NPCDialogueManager.rag` |
| `NPCNetworkSystem` | `NPCNetworkBootstrap`, `NPCNetworkPlayerController` | Networking root |
| `AuthUI` | `AuthUIController` | Login/register UI |
| `AuthBridge` | `AuthNetworkBridge` | Auth → network bridge |
| `PlayerSpawnPoint` | Transform | Player spawn location |
| `ChatCanvas` | TMP UI elements | Dialogue bubble canvas |
| `CogneeMemoryService` | `NPCCogneeService` | Endpoint `http://localhost:8000/api/v1` (not currently enabled in manager) |
| `FunctionCalling` | Function-calling LLMAgent | GameObject is inactive |
| `NPCNameplates` | `NPCNameplate` | World-space nameplates |

### 3.3 NPCDialogueManager Runtime Config

| Setting | Value |
|---------|-------|
| `useRemoteServer` | `true` |
| `remoteHost` | `localhost` |
| `remotePort` | `8080` |
| `remoteModel` | `llama-3.1-8b-q4-k-m` |
| `useQdrantRag` | `false` |
| `useCogneeMemory` | `false` |
| `forceRemoteEmbedder` | `false` |
| `profiles` | Butler, Maid, Chef |

**Key insight:** The manager uses direct HTTP to LocalAI when `useRemoteServer=true`. Local LLMUnity LLM is present but disabled. Qdrant and Cognee are available in code but disabled in the scene.

---

## 4. Backend Services

### 4.1 LocalAI

**Stack:** LocalAI docker-compose orchestrator at `/mnt/data/Projects_SSD/LocalAI/docker-compose.yaml`.

**Port:** `8080`

**Container:** `localai/localai:latest-gpu-nvidia-cuda-13`

**Model Store (source of truth):** `/mnt/data/models/localai/`

```
/mnt/data/models/localai/
├── llm/              # GGUF model binaries
│   ├── llama-3.1-8b/
│   ├── llama-3.2-3b/
│   ├── qwen2.5-1.5b/
│   ├── gemma-4/
│   └── qwen3-tts-cpp/
├── embeddings/       # Embedding models
├── codebase/         # Codebase indexing models
└── mmproj/           # Multi-modal projectors
```

**Key env vars:** `THREADS=8`, `gpu_layers=24` (8B models), `max_active_backends=2`, `watchdog_idle_timeout=15m`, `memory_reclaimer_threshold=0.85`

**Systemd service:** `localai-orchestrator.service` (user service, auto-start on boot)

**Active models in YAML configs** (at `LocalAI/models/*.yaml`): 16 models loaded including `llama-3.1-8b-q4-k-m`, `qwen2.5-1.5b`, `nomic-embed-text-v1.5`, etc.

### 4.2 Supabase

**Ports:** Gotrue on `:8091`, PostgREST on `:8092`

**Docker:** Part of the supabase-docker stack (run separately from LocalAI)

**Unity SDK packages:** `Supabase.dll`, `Supabase.Core.dll`, `Supabase.Gotrue.dll`, `Supabase.Postgrest.dll`, `Supabase.Realtime.dll`, etc. (precompiled refs in asmdef)

**Auth flow:** `PlayerAuthService` → `Supabase.Gotrue` → REST API. Local development addresses are auto-replaced from `localhost` to page host in WebGL builds via `NPCFlowLogger.IsLocalHost()`.

### 4.3 Qdrant

**Qdrant Client:** at `/mnt/data/Projects_SSD/qdrant-client/`

**Qdrant Storage:** at `/mnt/data/Projects_SSD/qdrant_storage/`

**Port:** `6333` (default)

**Service class:** `QdrantRAGService.cs` in `NPCSystem.Runtime`

**Collections:**
- `unity_linux_llm_codebase_v1` — stable, actively used for codebase RAG
- `unity_linux_llm_codebase_structural_v1` — experimental
- `unity_linux_llm_codebase_hierarchy_v1` — experimental

**Index config:** HNSW (ef_construct=200), scalar int8 quantization, `indexing_threshold=10000`

### 4.4 Cognee

**API:** `http://localhost:8000/api/v1`

**Systemd service:** `cognee-api.service` (user service)

**Postgres DB:** `cognee_db` on `127.0.0.1:5432` with pgvector extension

**LLM:** `qwen2.5-1.5b` (via LocalAI)

**Embeddings:** `nomic-embed-text-v1.5` (via LocalAI)

**Status:** Available but NOT enabled in active scene (`useCogneeMemory = false` in `NPCDialogueManager`)

---

## 5. Codebase Embedder

### 5.1 Tool Location

`Tools/CodebaseEmbedder/` at project root.

### 5.2 Running Commands

```bash
cd Tools/CodebaseEmbedder

# Status
uv run codebase-embedder status --root ../..

# Query
uv run codebase-embedder query --root ../.. --local "<concept>"

# Audit
uv run codebase-embedder audit --root ../.. --scene Assets/Scenes/NPCDialoguePrototype1.unity --scenario localai-llmunity --local

# Run tests
uv run --extra test pytest -q
```

Use `/tmp/uv-cache` and `/tmp/uv-tools` if default uv cache is read-only:
```bash
env UV_CACHE_DIR=/tmp/uv-cache UV_TOOL_DIR=/tmp/uv-tools uv run ...
```

### 5.3 Qdrant Index Structure

- **Named vectors:** `dense` (768d Cosine) + `code_keywords` (sparse)
- **Sparse vectors:** deterministic SHA-256 token hashing into 2M-dim space with TF normalization
- **Active profile:** `runtime` (promoted to default)
- **Default collection:** `unity_linux_llm_codebase_v1`
- **Experimental:** structural (boosted namespace/assembly records), hierarchy (namespace_summary aggregation)

### 5.4 CodebaseEmbedder Profile

```python
# Default profile = 'runtime'
# Enriched runtime_summary records carry:
#   "ASM — OWNS this runtime file"
#   "Declared namespace: X — declares types: Y"
#   "Namespace-uses: X imports Z"
```

---

## 6. Datadog Monitoring

### 6.1 Architecture

```
Unity Dedicated Server ──┬── DogStatsD (UDP 8125) ──▶ Datadog Agent ──▶ us5.datadoghq.com
                          │
NPCDialogueManager ───────┤  Custom metrics:
NPCDialogueSessionService ─┤  • llm.request.*         (duration, count, error)
QdrantRAGService ─────────┤  • dialogue.manager.*    (initialize, npc.switch)
AuthNetworkBridge ────────┤  • dialogue.session.*    (turn duration, count, error)
NPCNetworkBootstrap ──────┤  • dialogue.localai.*    (request duration, count)
                          │  • qdrant.search.*       (duration, count, error, result_count)
                          │  • auth.login.*          (count)
                          │  • network.*             (server, client, mode)
                          │
Docker Containers ──── stdout/stderr ──▶ json-file → Log tailing
                          │
All services ──────── APM traces ──▶ DD Agent (port 8126)
```

### 6.2 Datadog Agent

A single host-level Datadog Agent runs via `docker compose` in `Backend/datadog-host/`:

```yaml
container_name: dd-agent
image: gcr.io/datadoghq/agent:latest
network_mode: host
```

| Setting | Value |
|---------|-------|
| Site | `us5.datadoghq.com` |
| Env | `production` (configurable via `DD_ENV`) |
| Tags | `project:unity-linux-llm`, `team:npc-platform` |
| API Key | `Backend/.env` → `DD_API_KEY` |
| DogStatsD | Port `8125/udp` (non-local traffic enabled) |
| APM | Port `8126/tcp` (non-local traffic enabled) |
| Logs | `container_collect_all: true` |
| Process | `process_agent_enabled: true` |

**Integration configs** at `Backend/datadog-host/conf.d/`:

| Integration | File | What it monitors |
|-------------|------|-----------------|
| `localai.d/` | `conf.yaml` | LLM metrics + log parsing |
| `qdrant.d/` | `conf.yaml` | Collection points, search latency |
| `nginx.d/` | `conf.yaml` | Access/error log parsing |
| `unity.d/` | `conf.yaml` | DogStatsD (auto-discovered) |

### 6.3 Custom DogStatsD Metrics (from Unity)

Emitted by `DatadogMetricsService.cs` — a lightweight .NET Standard 2.0 client
at `Assets/Scripts/Runtime/Monitoring/DatadogMetricsService.cs`.

**Initialization:** `NPCDialogueBootstrapper.Awake()` calls `DatadogMetricsService.Initialize()`
(default: `127.0.0.1:8125`). Shutdown in `OnDestroy()`.

| Unity Script | Metrics | Key Tags |
|-------------|---------|----------|
| `NPCLocalAIClient` | `llm.request.duration`, `llm.request.count`, `llm.request.error` | model, status, reason |
| `NPCDialogueManager` | `dialogue.manager.initialize.duration`, `dialogue.manager.initialize.count`, `dialogue.npc.switch.count` | profile_count, use_qdrant, npc |
| `NPCDialogueSessionService` | `dialogue.session.turn.duration`, `dialogue.session.turn.count`, `dialogue.session.error`, `dialogue.localai.request.duration`, `dialogue.localai.request.count` | npc, action_type, model |
| `QdrantRAGService` | `qdrant.search.duration`, `qdrant.search.count`, `qdrant.search.error`, `qdrant.search.result_count` | limit, reason, collection |
| `AuthNetworkBridge` | `auth.login.count` | mode |
| `NPCNetworkBootstrap` | `network.server.started`, `network.client.connected`, `network.client.disconnected`, `network.mode.start` | mode, listen_port |

### 6.4 Importing the Dashboard

The comprehensive dashboard is at `Backend/datadog-host/dashboard.json`.

To import:
1. Open Datadog → Dashboards → New Dashboard → Import Dashboard JSON
2. Paste `dashboard.json` contents
3. Save as "NPC Platform — Unity Linux LLM"

**Widget groups:**
- 🚀 **Service Overview** — Container resource usage, health checks
- 🤖 **LLM / LocalAI** — Request latency (avg/p95/p99), throughput, error rate
- 💬 **Dialogue System** — Turn volume, duration, errors, most active NPCs
- 🗄️ **Qdrant Vector DB** — Search latency, throughput, result counts, collection size
- 🔐 **Auth & Network** — Login volume, server starts, client connections
- 🖥️ **Linux Host OS** — CPU, memory, disk I/O, network traffic, GPU, disk space

### 6.5 Single Host-Level Agent (Simplified)

The architecture uses **one host-level agent** (`dd-agent` in `Backend/datadog-host/`) that
auto-discovers all Docker containers — no sidecar agents needed. With
`DD_LOGS_CONFIG_CONTAINER_COLL_ALL=true`, the agent automatically tails every
container's stdout/stderr, and the custom DogStatsD metrics from Unity reach it
via UDP port 8125 on the host network.

**No Datadog Agent sidecars** exist in any of the per-service compose files
(`unity-dedicated-server`, `webgl-client`, `codebase-watchdog`, `supabase-stack`).
They were removed because multiple agents in `network_mode: host` conflict on ports
8125/8126. If you need per-stack agents for development isolation, assign each a
unique port mapping instead of host networking.

---

## 7. Testing

### 6.1 Test Suite (23 files, 149 tests)

All tests are `[NUnit.Framework]` Editor tests under `Assets/Scripts/Tests/Editor/`.

| Test File | Tests | What It Covers |
|-----------|-------|----------------|
| `AuthNetworkBridgeTests.cs` | 7 | CLI startup-mode parsing, player name, resolve fallback |
| `NPCDialogueActionPlannerTests.cs` | 19 | All 7 action types, keyword matching, profile gating |
| `NPCDialogueManagerTests.cs` | 14 | Dialogue manager initialization, config |
| `NPCDialogueNetworkingTests.cs` | 16 | Network message serialization |
| `NPCFlowLoggerTests.cs` | 5 | Logging pipeline, text sanitization |
| `NPCFlowLoggerPlatformTests.cs` | 2 | WebGL platform guards |
| `NPCHistoryStoreTests.cs` | 15 | History CRUD, normalization |
| `NPCLocalAIClientTests.cs` | 7 | LocalAI client calls |
| `NPCLocalAIEmbedderTests.cs` | 7 | Embedder API calls |
| `NPCMainSceneWiringTests.cs` | 1 | Scene wiring validation |
| `NPCNetworkingTests.cs` | 6 | Transport config, play-mode resolver |
| `NPCNetworkPlayerPrefabTests.cs` | 8 | Spawn validation |
| `NPCNetworkNpcPrefabTests.cs` | 1 | NPC spawn |
| `NPCNetworkTransferableItemPrefabTests.cs` | 1 | Item spawn |
| `NPCNotebookStateTests.cs` | 2 | Notebook state management |
| `NPCPlayerInventoryTests.cs` | 1 | Inventory |
| `NPCSceneInitializationTests.cs` | 3 | Init controller behavior |
| `NPCStartupAuthorityStaticTests.cs` | 1 | Authority constants |
| `NPCBackendReadinessTests.cs` | 2 | Backend probe result logic |
| `NPCWebGLSmokeTests.cs` | 5 | WebGL async, localhost, platform guards |
| `PlayerAuthServiceStaticTests.cs` | 14 | Auth static utilities (email, URL, session data) |
| `QdrantRAGServiceTests.cs` | 13 | RAG query/response |
| `NPCTestHelpers.cs` | — | Test utility class (0 tests) |

### 6.2 Test Patterns

**Standard setup — instantiate in-memory:**
```csharp
[Test]
public void MyTest()
{
    var go = new GameObject("TestObj");
    var component = go.AddComponent<MyComponent>();
    try
    {
        // assertions
    }
    finally
    {
        Object.DestroyImmediate(go);
    }
}
```

**Static method testing (via reflection):**
```csharp
static T Invoke<T>(MethodInfo method, params object[] args)
    => (T)method.Invoke(null, args);
```

**Parameterized:**
```csharp
[Test]
[TestCase("input1")]
[TestCase("input2")]
public void MyParamTest(string input)
{
    ...
}
```

### 6.3 Test Clarity Rules

1. Every test file is in `namespace NPCSystem.Tests`
2. `Object.DestroyImmediate()` in `finally` block to prevent leak
3. Clear test method names: `Subject_Scenario_ExpectedBehavior()`
4. No magic strings — use well-named constants or inline documentation
5. Tests are first-class citizens — maintain alongside production code

---

## 7. Networking & Auth

### 7.1 NPCNetworkBootstrap

**Source:** `Assets/Scripts/Runtime/Networking/NPCNetworkBootstrap.cs`

**Execution order:** `Awake()` at `[-2500]`, `Start()` at `[-2000]`

**Key responsibilities:**
- `Awake()`: ApplyTransportConfiguration
- `Start()`: Auto-start in server mode if CLI flag `-npc-server` present
- `ApplyTransportConfiguration()`: Transfers `NPCTransportConfig` → `UnityTransport`
- CLI args: `-npc-server`, `-npc-websockets`, `-port`, `-address`, `-npc-client`, `-npc-host`

### 7.2 NPCTransportConfig

| Field | Default | Description |
|-------|---------|-------------|
| `connectAddress` | `"127.0.0.1"` | Server address to connect to |
| `listenAddress` | `"0.0.0.0"` | Address to listen on |
| `port` | `11474` | Network port |
| `useWebSockets` | `false` | Enable WebSockets (forced `true` on WebGL) |
| `webSocketPath` | `"/npc-dialogue"` | WebSocket path |
| `autoStartMode` | `Manual` | Auto-start behaviour |

### 7.3 WebGL Transport

In `ApplyTransportConfiguration()`:
```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
    TransportConfig.useWebSockets = true;
#endif
```
WebGL uses UnityTransport with WebSockets — forced at compile time for builds, but NOT in Editor (so Editor tests can test without WebSockets).

### 7.4 Auth Flow

1. `PlayerAuthService.InitializeAsync()` — sets up Supabase Gotrue client
2. `AuthUIController` captures login/register UI events
3. `AuthNetworkBridge.HandleAuthSuccess(username)` — stores player name
4. `AuthNetworkBridge.ResolveStartupMode()` — determines Host vs Client:
   - CLI `-npc-host`/`-npc-client` → highest priority
   - Multiplayer Play Mode player index → 1=Host, 2+=Client
   - `AutoDetectStartupMode` → MultiplayerRoleFlags
   - `_startAsHost` boolean → final fallback
5. `StartHostAndRegisterPlayerName()` or `StartClientAndRegisterPlayerName()`

### 7.5 Cross-Platform URL Resolution

In WebGL builds, `"localhost"` inside the browser refers to the browser's own machine, not the server. The runtime auto-detects this:

```csharp
if (NPCFlowLogger.IsLocalHost(RemoteHost))
{
    Uri pageUri = new Uri(Application.absoluteURL);
    if (!NPCFlowLogger.IsLocalHost(pageUri.Host))
        RemoteHost = pageUri.Host;  // Replace with real host
}
```

Applied in `NPCDialogueManager.cs` (RemoteHost + RemoteEmbeddingHost), `PlayerAuthService.cs` (supabaseUrl), and `AuthNetworkBridge.cs` (hostAddress).

---

## 8. Editor Tooling

### 8.1 GladeKit MCP

**Primary Unity Inspector/Scene access path.** Always use GladeKit MCP tools (via `mcp__gladekit_mcp__*`) for:
- Scene hierarchy (`get_scene_hierarchy`, `find_game_objects`)
- Component state (`get_component_inspector_properties`, `get_gameobject_info`)
- Script compilation (`compile_scripts`)
- Play Mode state (`get_play_mode_state`, `get_runtime_events`)
- Creating/mutating scene objects
- Script creation and editing

**Rules:**
- `compile_scripts` MUST be `idle` with `hasErrors=false` before `add_component`
- `create_game_object` creates EMPTY (invisible) objects — use `create_primitive` for visible geometry
- Never manually edit `.unity` scene files — GladeKit handles scene mutation safely

### 8.2 Editor Scripts

| Script | Location | Purpose |
|--------|----------|---------|
| `NPCFactoryWindow.cs` | `Assets/Scripts/Editor/` | Generate NPC profiles + knowledge via LLM |
| `MysterySceneTemplateGenerator.cs` | `Assets/Scripts/Editor/` | Scene template generation |
| `HeadlessPlayMode.cs` | `Assets/Scripts/Editor/` | Headless play mode automation |
| `TestQdrantRAG.cs` | `Assets/Scripts/Editor/` | Qdrant RAG test tool |
| `CreateNPCTool.cs` | `Assets/Scripts/Editor/Tools/` | Auto-registers with Glade, creates profile + knowledge |

### 8.3 GladeKit Bridge Safety

In `Unity_Linux_LLM`, the GladeKit bridge should NOT auto-start in secondary Multiplayer Play Mode editor instances. Only the primary editor instance should host the bridge.

---

## 9. Safety Gates

The following require explicit user approval before proceeding:

| Action | Reason |
|--------|--------|
| Mutating Unity scene files | GameObjects/wiring is fragile and scene diffs are dense |
| Changing runtime architecture | Affects all downstream code and scene wiring |
| Deleting/moving/archiving files | Can break git history and references |
| Adding Ollama or removing it | Architecture decision — needs ADR |
| Running LoRA training | GPU-heavy, long-duration |
| Committing or pushing | User controls the commit cadence |

**Docs-only changes** are always allowed when documenting evidence.

---

## 10. Known Compile Warnings (Non-Blocking)

| Code | Location | Issue |
|------|----------|-------|
| `CS0618` | Several editor/runtime scripts | `FindFirstObjectByType`/`FindObjectOfType` is obsolete |
| `CS0108` | `NPCDialogueManager.cs` | `SendMessage(string)` hides `Component.SendMessage(string)` — rename or `new` in controlled pass |

---

## 11. Dedicated Server

**Build:** Server binary outputs to `Builds/Server/`
**Runtime flags:** `-batchmode -npc-server -port 11474 -address 0.0.0.0 -npc-websockets`
**Docker:** `docker/Dockerfile` (ubuntu:22.04), `docker-compose.yml` (network_mode: host reaches host LocalAI at `localhost:8080`/`:11435`)
**Architecture key:** `NPCNetworkBootstrap` auto-starts in `Start()` (not `Awake()`) to avoid NRE — `NetworkManager`'s `Awake` at order 0 runs first.

---

*Document generated from completed code-quality-improvement phases 1-9 (2026-07-09).*
