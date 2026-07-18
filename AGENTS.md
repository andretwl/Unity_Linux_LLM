# Unity_Linux_LLM Agent Reference

This file is the project-wide source of truth for agents. Keep it short, stable, and operational. Deep details live in the linked docs.

## Quick links

| Topic | Read first |
|---|---|
| Architecture / live services | `Documentation/2_Architecture/Backend_Services_Topology.md` |
| Code rules | `#1-code-conventions` |
| Scene wiring | `#3-scene-wiring-current` |
| Tests | `#7-testing` |
| Networking / auth | `#8-networking--auth` |
| Editor tooling | `#9-editor-tooling` |
| Safety gates | `#10-safety-gates` |
| Addressables / build level | `#13-addressables--compatibility-level` |

---

## 1. Code conventions

- Names: private `_camelCase`; serialized private `[FormerlySerializedAs("OldName")] [SerializeField] private _camelCase`; public members PascalCase; locals/params camelCase; namespaces `NPCSystem.*`.
- Serialized fields: always keep the `FormerlySerializedAs` attribute when renaming inspector-backed fields.
- Async: use `try/finally`; `await Task.Yield()` is WebGL-safe; avoid `ConfigureAwait(false)`.
- Formatting: spaces, 4-space indent, Allman braces, LF, trimmed whitespace, final newline.
- Docs: public APIs get XML docs where useful; delete redundant comments.
- Anti-patterns: no bool flag params, no hard-coded `"localhost"` strings, no commented-out code, no TODO/FIXME/HACK, no single-letter vars except loop counters.
- If `#1` changes, run `dotnet run --project Tools/NPCDialogueCodeReview -- --verify-docs`.

## 2. Project map

- Active scene: `Assets/Scenes/NPCDialoguePrototype1.unity`.
- Main runtime folders: `Assets/Scripts/Runtime/` (`Initialization`, `LocalRAG`, `Networking`, `NPCDialogue`, `Supabase`), `Assets/Scripts/Editor/`, `Assets/Scripts/Tests/Editor/`.
- Key asmdefs: `NPCSystem.Runtime`, `NPCSystem.Editor`, `NPCSystem.Tests`.
- Namespaces: `NPCSystem`, `NPCSystem.Editor`, `NPCSystem.Tests`.
- Dedicated server output: `Builds/Server/`; Docker lives in `docker/`.

## 3. Scene wiring (current)

- `NPCDialogueSystem`: `NPCDialogueManager`, `NPCDialogueBootstrapper`, `QdrantRAGService`, `NPCDialogueActionPlanner`.
- `NPCNetworkSystem`: `NPCNetworkBootstrap`.
- `AuthUI`: `AuthUIController`; `AuthBridge`: `AuthNetworkBridge`.
- `LLM` and `LLMAgent` exist but are disabled; `NPCDialogueManager` currently uses HTTP to LocalAI.
- `LLMRAG` is the active local embedding model; `CogneeMemoryService` exists but is disabled in the scene.
- `Assets/Scenes/NPCDialoguePrototype1.unity` is the only authoritative scene file. Do not edit `.unity` files by hand.

## 4. Backend services

- LocalAI: `localhost:8080` (`/mnt/data/Projects_SSD/LocalAI/docker-compose.yaml`), model store `/mnt/data/models/localai/`.
- Supabase: Gotrue `:8091`, PostgREST `:8092`.
- Qdrant: client `/mnt/data/Projects_SSD/qdrant-client/`, storage `/mnt/data/Projects_SSD/qdrant_storage/`, port `6333`.
- Cognee: `http://localhost:8000/api/v1`, Postgres `127.0.0.1:5432`, disabled in the active scene.
- WebGL host rewriting uses `NPCNetworkUtils.IsLocalHost()`; do not reintroduce raw `"localhost"` checks.

## 5. Codebase embedder

- Tool root: `Tools/CodebaseEmbedder/`.
- Common commands:
  - `uv run codebase-embedder status --root ../..`
  - `uv run codebase-embedder query --root ../.. --local "<concept>"`
  - `uv run codebase-embedder audit --root ../.. --scene Assets/Scenes/NPCDialoguePrototype1.unity --scenario localai-llmunity --local`
  - `uv run --extra test pytest -q`
- Use `UV_CACHE_DIR=/tmp/uv-cache` and `UV_TOOL_DIR=/tmp/uv-tools` if the default uv cache is read-only.
- Current defaults: profile `runtime`, collection `unity_linux_llm_codebase_v2`. Live collections: `npc_knowledge` and `unity_linux_llm_codebase_v2`.

## 6. Datadog monitoring

- Single host-level agent: `Backend/datadog-host/` (`dd-agent`), no sidecars.
- Ports: DogStatsD `8125/udp`, APM `8126/tcp`.
- Unity emits custom metrics from `Assets/Scripts/Runtime/Monitoring/DatadogMetricsService.cs` and spans from `DatadogTraceService.cs`.
- Dashboard JSON: `Backend/datadog-host/dashboard.json`.
- Key metric families: `llm.request.*`, `dialogue.*`, `qdrant.search.*`, `auth.login.*`, `network.*`.

## 7. Testing

- All tests live under `Assets/Scripts/Tests/Editor/` and use `[NUnit.Framework]`.
- Standard pattern: instantiate with `new GameObject() + AddComponent<T>()`, then `Object.DestroyImmediate()` in `finally`.
- Keep test names explicit: `Subject_Scenario_ExpectedBehavior()`.
- No magic strings unless documented.
- `Tools/NPCDialogueCodeReview` is the code-rule verifier; run it manually after touching `NPCDialogue` scripts or rule docs.

## 8. Networking & auth

- `NPCNetworkBootstrap`: `Awake()` applies transport config; `Start()` auto-starts server mode when `-npc-server` is present.
- CLI args: `-npc-server`, `-npc-websockets`, `-port`, `-address`, `-npc-client`, `-npc-host`.
- `NPCTransportConfig` defaults: connect `127.0.0.1`, listen `0.0.0.0`, port `11474`, WebSockets off unless WebGL.
- WebGL forces WebSockets in `ApplyTransportConfiguration()`.
- Auth flow: `PlayerAuthService.InitializeAsync()` → `AuthUIController` → `AuthNetworkBridge.HandleAuthSuccess()` → host/client selection.
- WebGL URL rewrite: if the runtime host is local, replace `RemoteHost` with `Application.absoluteURL` host via `NPCNetworkUtils.IsLocalHost()`.

## 9. Editor tooling

- Use GladeKit MCP for scene hierarchy, component inspection, Play Mode state, object creation, and script creation/editing.
- `compile_scripts` must be idle with `hasErrors=false` before `add_component`.
- Use `create_primitive` for visible geometry; `create_game_object` creates empty objects.
- Never edit `.unity` scene files directly.
- Editor scripts live in `Assets/Scripts/Editor/` and `Assets/Scripts/Editor/Tools/`.

## 10. Safety gates

- Explicit approval required for: mutating Unity scene files, changing runtime architecture, deleting/moving/archiving files, adding or removing Ollama, running LoRA training, committing, or pushing.
- Docs-only changes are always allowed.

## 11. Known compile warnings

- `CS0618`: `FindFirstObjectByType` / `FindObjectOfType` usage in several scripts.
- `CS0108`: `NPCDialogueManager.SendMessage(string)` hides `Component.SendMessage(string)`; avoid the inherited `Component.SendMessage()`.

## 12. Dedicated server

- Build output: `Builds/Server/`.
- Runtime flags: `-batchmode -npc-server -port 11474 -address 0.0.0.0 -npc-websockets`.
- Docker: `docker/Dockerfile`, `docker-compose.yml`, `docker/entrypoint.sh`.
- `NPCNetworkBootstrap` starts in `Start()` so `NetworkManager.Awake()` runs first.

## 13. Addressables / compatibility

- All build profiles must use `apiCompatibilityLevel: 2` (.NET Standard 2.1). Level 6 breaks Addressables, Unity Transport, Serialization, and RP Core packages.
- After changing `apiCompatibilityLevel`, delete `Library/` and rebuild.
- `m_BuildAddressablesWithPlayerBuild` should be `0` for iteration builds. Rebuild Addressables manually when needed.
- If SBP errors appear, clear `Library/com.unity.addressables/`, `Temp/com.unity.addressables/`, and stale `addressables_content_state.bin` files, then rebuild.

---

*Generated from completed code-quality-improvement phases 1-9 (2026-07-09).*
