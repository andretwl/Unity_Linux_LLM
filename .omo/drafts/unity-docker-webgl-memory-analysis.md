intent: clear
review_required: false
status: awaiting-approval
pending_action: write .omo/plans/unity-docker-webgl-memory-analysis.md

summary:
- Target outcome: decision-complete plan to analyze and fix the locally Docker-hosted Unity dedicated server plus WebGL client load failure.
- Primary symptom: browser-side "memory out of bounds" / WebGL load failure on the webpage served from the container.

grounding:
- Dedicated server container uses `network_mode: host`; WebGL client is static nginx on `8085` with `/ws` reverse proxy to `host.docker.internal:11474`.
- Repo WebGL output path is `Builds/WebGL_client/LinuxWebGLWS`; active generated assets are under `Build/` and use Brotli filenames (`.data.br`, `.framework.js.br`, `.wasm.br`).
- Current project WebGL settings: `webGLInitialMemorySize: 512`, `webGLMaximumMemorySize: 2048`, `webGLMemoryGrowthMode: 2`, `webGLThreadsSupport: 0`, `webGLDecompressionFallback: 0`.
- Current generated WebGL payload is modest in compressed size (`~14M wasm.br`, `~6M data.br`), so raw artifact size alone does not explain the failure.
- Runtime code already contains WebGL-specific mitigations and deferred-init paths:
  - `Assets/Scripts/Runtime/Initialization/NPCSceneInitializationController.cs`
  - `Assets/Scripts/Runtime/NPCDialogue/AOTGenericPreservation.cs`
  - `Assets/Scripts/Runtime/Initialization/NPCBackendReadinessService.cs`
- Current Unity docs confirm:
  - WebGL max memory can be configured up to `4096 MB`
  - compressed assets need correct `Content-Encoding` and MIME headers
  - COOP/COEP headers are required for multithreaded wasm, though this project currently has threads disabled

recommended_approach:
- Treat this as a three-surface problem:
  1. static asset hosting and headers in nginx
  2. Unity WebGL heap/startup behavior
  3. runtime initialization/network code that may spike memory or fail after the loader starts
- Prefer evidence-first diagnosis over more blind rebuild permutations.

test_strategy: none
qa_mode: agent-executed verification of build artifacts, container headers, browser console/network evidence, and targeted rebuild comparison
