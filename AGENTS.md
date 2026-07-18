# Unity_Linux_LLM Agent Reference

This file is the project-wide source of truth for agents. Keep it short, stable, and operational. Deep details live in the linked docs.

## Quick links

| Topic | Read first |
|---|---|
| Architecture / live services | `Documentation/2_Architecture/Backend_Services_Topology.md` |
| Code rules | `#1-code-conventions` |
| Codebase scanning/rules | `#5-codebase-embedder` |
| Scene wiring | `#3-scene-wiring-current` |
| Tests | `#7-testing` |
| Networking / auth | `#8-networking--auth` |
| Editor tooling | `#9-editor-tooling` |
| Safety gates | `#10-safety-gates` |
| Addressables / build level | `#13-addressables--compatibility-level` |

---

## 1. Code conventions

Dual source of truth (do not duplicate rule text here):

| Concern | Owner |
|---|---|
| Formatting + naming (IDE) | `.editorconfig` |
| Unity-specific + architectural rules (CI) | `.codebaserules.yaml` |

- Namespaces for project code: `NPCSystem.*` (Runtime / Editor / Tests).
- If conventions change: update `.editorconfig` first, then `.codebaserules.yaml` if needed.
- After touching rules: `uv run --directory Tools/CodebaseEmbedder codebase-embedder check --root ..`.
- Agents: retrieve policy via `codebase-embedder query` (`project_rule` records) or read the two files above.

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
- `LLM` and `LLMAgent` exist but are disabled; `NPCDialogueManager` currently uses HTTP to LocalAI **via port 8080** (direct, no proxy — see `NPCLocalAIConfig.LocalAIDirectPort`).
- `LLMRAG` is the active local embedding model; `CogneeMemoryService` exists but is disabled in the scene.
- `Assets/Scenes/NPCDialoguePrototype1.unity` is the only authoritative scene file. Do not edit `.unity` files by hand.

## 4. Backend services

- LocalAI (upstream): **port 8080** — all gameplay dialogue, embeddings, and health checks go directly to this port. Static config in `NPCLocalAIConfig.LocalAIDirectPort`.
- localai-proxy (observability): **port 8090** — provides token/latency tracking for CLI tools, codebase-watchdog, and ad-hoc debugging. NOT in the gameplay path. Static config in `NPCLocalAIConfig.LocalAIProxyPort`.
- Supabase: Gotrue `:8091`, PostgREST `:8092`.
- Qdrant: client `/mnt/data/Projects_SSD/qdrant-client/`, storage `/mnt/data/Projects_SSD/qdrant_storage/`, port `6333`.
- Cognee: `http://localhost:8000/api/v1`, Postgres `127.0.0.1:5432`, disabled in the active scene.
- WebGL host rewriting uses `NPCNetworkUtils.IsLocalHost()`; do not reintroduce raw `"localhost"` checks.

## 5. Codebase embedder (unified toolchain)

- Tool root: `Tools/CodebaseEmbedder/`.
- **Roslyn parser** (`Tools/CodebaseEmbedder/roslyn_parser/`): C# .NET 10 console app using Microsoft.CodeAnalysis.CSharp 4.9.2.
  Extracts **10 symbol types** (file_overview, namespace, type, member, field, serialized_field, constructor, property, event, using_directive) and **5 relation kinds** (inherits, implements, calls, namespace-contains-type, type-contains-member).
  Includes cross-file symbol resolution: `calls` relation targets are linked to their declaration stable keys.

### Commands

| Command | What it does |
|---|---|
| `scan` | Discover files, run Roslyn parser, write `.codebase-index/` artifacts |
| `index` | Embed artifacts via LocalAI and upsert to Qdrant |
| `query` | Semantic or lexical search against Qdrant or local artifacts |
| `check` | Config-driven rule checking against `.codebaserules.yaml` |
| `audit` | Scene-aware retrieval audit |
| `status` | File counts + Qdrant health |

### Common usage

```
uv run codebase-embedder status --root ../..
uv run codebase-embedder scan --root ../..
uv run codebase-embedder index --root ../..   # embeds + upserts to Qdrant
uv run codebase-embedder index --root ../.. --clear   # delete all points first, then re-index
uv run codebase-embedder query --root ../.. --local "<concept>"
uv run codebase-embedder check --root ../..   # runs all 21 rules from .codebaserules.yaml
uv run codebase-embedder check --root ../.. --target Assets/Scripts/Runtime --output check-report.md
uv run codebase-embedder audit --root ../.. --scene Assets/Scenes/NPCDialoguePrototype1.unity --scenario localai-llmunity --local
uv run --extra test pytest -q
```

- Use `UV_CACHE_DIR=/tmp/uv-cache` and `UV_TOOL_DIR=/tmp/uv-tools` if the default uv cache is read-only.
- Current defaults: profile `runtime`, collection `unity_linux_llm_codebase_v2`. Live collections: `npc_knowledge` and `unity_linux_llm_codebase_v2`.

### Rule checking (.codebaserules.yaml)

- Rules defined in `.codebaserules.yaml` at project root — **10 rules** covering Unity-specific serialization (SER01-SER02), anti-patterns (BPR01, TODO01, CMT01, NET01, AWS01), and special cases (NAM07, VAR01, SND01, DOC01). Formatting and naming conventions are enforced by `.editorconfig` (not duplicated here).
- Three check types: `text` (raw substring), `regex` (per-line match), `roslyn` (structural — requires full `scan`).
- Rule reference: `AGENTS.md §1.1` (naming via `.editorconfig`), `§1.4` (formatting via `.editorconfig`), `§1.6` (anti-patterns via `.codebaserules.yaml`).

### Artifact structure (.codebase-index/)

| File | Contents |
|---|---|
| `manifest.json` | Collection metadata, counts, timestamp |
| `symbols.jsonl` | All extracted symbol records (types, members, fields, etc.) |
| `relations.jsonl` | Full relation graph (calls, inherits, implements, containment) |
| `chunks.jsonl` | Embedding-ready text chunks for Qdrant |
| `asmdefs.json` | Assembly definition metadata |

## 6. Datadog monitoring

- Single host-level agent: `Backend/datadog-host/` (`dd-agent`), no sidecars.
- Ports: DogStatsD `8125/udp`, APM `8126/tcp`.
- Unity emits custom metrics from `Assets/Scripts/Runtime/Monitoring/DatadogMetricsService.cs` and spans from `DatadogTraceService.cs`.
- Dashboard JSON: `Backend/datadog-host/dashboard.json`.
- Key metric families: `llm.request.*`, `dialogue.*`, `qdrant.search.*`, `auth.login.*`, `network.*`.
- SAST: `code-security.datadog.yaml` uses rulesets `csharp-best-practices`, `csharp-code-style`, `csharp-security`, `csharp-inclusive`.
- SAST operates on a different layer from `.editorconfig` (formatting) and `.codebaserules.yaml` (arch rules). Naming rules in `csharp-code-style` use MS conventions (`s_` for static fields) — our project uses `_camelCase` uniformly. If SAST reports false positives on naming, tune via `.datadog/static-analysis.yml` or update `code-security.datadog.yaml` per-rule overrides.

## 7. Testing

- All tests live under `Assets/Scripts/Tests/Editor/` and use `[NUnit.Framework]`.
- Standard pattern: instantiate with `new GameObject() + AddComponent<T>()`, then `Object.DestroyImmediate()` in `finally`.
- Keep test names explicit: `Subject_Scenario_ExpectedBehavior()`.
- No magic strings unless documented.
- After touching NPCDialogue scripts or rule docs, run `uv run --directory Tools/CodebaseEmbedder codebase-embedder check --root ..`.

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

## 14. Code quality workflow

Repeatable audit → fix → verify cycle using existing tools:

1. **Scan** — `uv run codebase-embedder scan --root ../..` (refreshes `.codebase-index/` artifacts)
2. **Check** — `uv run codebase-embedder check --root ../.. --target <path> --output report.md` (runs `.codebaserules.yaml` rules)
3. **Fix** — address violations in reported files (SER01 → private fields, NET01 → remove hard-coded localhost, BPR01 → split bool params, CMT01 → delete commented code)
4. **Re-check** — run `check` again, confirm 0 violations
5. **Scene audit** (when Unity Editor is open) — use GladeKit MCP: `get_scene_hierarchy` → `find_game_objects` → `get_component_inspector_properties` → verify field values match code expectations
6. **Re-index** — `uv run codebase-embedder index --root ../.. --clear` for a clean re-index when artifacts change significantly

Key flags:
- `--target <path>` — scope `check` to a subdirectory
- `--output <file>` — write report to file
- `--clear` — delete all Qdrant points before re-indexing
- `--no-qdrant` — skip Qdrant upsert (local artifacts only)

---

*Generated from completed code-quality-improvement phases 1-9 (2026-07-09). Unified codebase workflow: roslyn_parser + codebase-embedder check replaces deprecated NPCDialogueCodeReview.*
