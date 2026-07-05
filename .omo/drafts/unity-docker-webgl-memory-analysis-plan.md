---
slug: unity-docker-webgl-memory-analysis-plan
status: planned
intent: clear
pending-action: deliver .omo/plans/unity-docker-webgl-memory-analysis-plan.md and wait for user to start execution
approach: diagnose the WebGL load failure through the real Docker-hosted browser surface, split evidence across nginx/static hosting, WebGL heap configuration, and runtime startup pressure, then apply the smallest fix proven by comparison builds and live verification
---

# Draft: unity-docker-webgl-memory-analysis-plan

## Components (topology ledger)
<!-- Lock the SHAPE before depth. One row per top-level component that can succeed or fail independently. -->
<!-- id | outcome (one line) | status: active|deferred | evidence path -->
- `webgl-hosting` | nginx serves the exact generated Unity WebGL artifacts with correct encoding, MIME, and cross-origin headers | active | `docker/docker-compose.yml:27`, `docker/nginx.conf:32`
- `webgl-build-config` | Unity emits a reproducible WebGL build with controlled memory settings and comparable outputs | active | `Assets/Editor/NPCDialogueBuild.cs:59`, `ProjectSettings/ProjectSettings.asset:820`
- `runtime-startup` | WebGL startup avoids unnecessary heavy initialization before the first usable frame/login | active | `Assets/Scripts/Runtime/Initialization/NPCSceneInitializationController.cs:44`, `Assets/Scripts/Runtime/Initialization/NPCBackendReadinessService.cs:42`
- `webgl-il2cpp-compat` | WebGL-only IL2CPP/AOT crash and generic-sharing risks are ruled in or out separately from heap sizing | active | `Assets/Scripts/Runtime/NPCDialogue/AOTGenericPreservation.cs:6`
- `dedicated-server-path` | the server container is reachable through the expected websocket/network path and not masking the client-side load failure | active | `docker/docker-compose.yml:2`, `Tests/Integration/run-dedicated-server-smoke.sh:4`

## Open assumptions (announced defaults)
<!-- Record any default you adopt instead of asking, so the user can veto it at the gate. -->
<!-- assumption | adopted default | rationale | reversible? -->
- `test_strategy` | no new automated tests unless a code fix creates a stable regression boundary | this task is primarily deployment/runtime diagnosis first, not unit behavior work | yes
- `browser_surface` | use a local desktop Chromium-class browser as the primary reproduction target | the failure is webpage load behavior and Chromium exposes console/network/memory evidence clearly | yes
- `dedicated_server_scope` | server changes are only in scope if they directly affect WebGL startup or websocket connection establishment | the lead symptom is client page load failure, not generic dedicated-server hardening | yes

## Findings (cited - path:lines)
- The Docker topology is split: the dedicated server runs with `network_mode: host` and binds `/server` from `Builds/ServerWS`, while the WebGL client is an nginx container exposing `8085` and mounting `Builds/WebGL_client/LinuxWebGLWS` as static content (`docker/docker-compose.yml:2-37`).
- nginx already contains Unity-specific handling for `/Build/` artifacts, precompressed `.br` and `.gz` assets, COOP/COEP headers, and `/ws` reverse proxying to `host.docker.internal:11474` (`docker/nginx.conf:62-257`).
- The repo build entry point for WebGL is `NPCDialogueBuild.BuildWebGL()`, which writes to `Builds/WebGL_client/LinuxWebGLWS` and currently uses `BuildOptions.None` (`Assets/Editor/NPCDialogueBuild.cs:59-75`).
- Current project WebGL settings are conservative but not tiny: `webGLInitialMemorySize=512`, `webGLMaximumMemorySize=2048`, `webGLMemoryGrowthMode=2`, `webGLDecompressionFallback=0`, `webGLThreadsSupport=0` (`ProjectSettings/ProjectSettings.asset:820-829`).
- Runtime startup code already documents deferred dialogue initialization as the recommended WebGL memory-smart path and allows backend probing to be deferred (`Assets/Scripts/Runtime/Initialization/NPCSceneInitializationController.cs:44-52`, `Assets/Scripts/Runtime/Initialization/NPCSceneInitializationController.cs:134-168`, `Assets/Scripts/Runtime/Initialization/NPCBackendReadinessService.cs:99-183`).
- The repo contains a dedicated WebGL IL2CPP workaround for generic-sharing memory violations, which means not all “memory out of bounds” reports should be treated as simple heap exhaustion (`Assets/Scripts/Runtime/NPCDialogue/AOTGenericPreservation.cs:13-17`).
- Dedicated-server smoke validation already exists and is enough to verify whether the server side is alive before blaming the webpage load on transport reachability (`Tests/Integration/run-dedicated-server-smoke.sh:4-24`).

## Decisions (with rationale)
- The plan will diagnose through the real browser and container surface first, because repeated build-setting changes without runtime/header evidence are low-signal for this failure class.
- The plan will split loader/instantiation failures from post-boot runtime failures, because the fix space differs materially between asset hosting, heap sizing, and game startup logic.
- The first controlled rebuild matrix will vary WebGL memory settings and one hosting/compression diagnostic at a time instead of mixing multiple toggles, to preserve causality.
- The plan will treat runtime initialization deferral as a first-class fix candidate, not only as a last resort, because the repo already encodes it as the WebGL-smart path.

## Scope IN
- Analyze the Docker-hosted dedicated server plus WebGL client path end-to-end as it affects page load and first connection.
- Inspect nginx headers, compression handling, and build artifact naming against the current generated WebGL files.
- Capture browser-side evidence: exact error text, network failures, console logs, and whether Unity reaches loader, wasm instantiation, first frame, or websocket connection.
- Run controlled WebGL rebuild comparisons focused on memory sizing and startup behavior.
- Patch build settings, hosting config, or startup/runtime code if evidence proves they are the root cause.

## Scope OUT (Must NOT have)
- No unrelated multiplayer redesign, auth redesign, or Qdrant/Cognee feature work.
- No blind asset/content reductions that are not supported by measured startup evidence.
- No replacing Docker/nginx hosting with a different stack unless the current stack is proven fundamentally incompatible and a minimal fix is impossible.
- No broad cleanup or refactor of unrelated runtime systems.

## Open questions
- None blocking plan generation. The repo answers the technical forks needed to produce the plan.

## Approval gate
status: approved by user on 2026-07-05
<!-- When exploration is exhausted and unknowns are answered, set status: awaiting-approval. -->
<!-- That durable record is the loop guard: on a later turn read it and resume at the gate instead of re-running exploration. -->
