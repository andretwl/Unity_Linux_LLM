# unity-docker-webgl-memory-analysis-plan - Work Plan

## TL;DR (For humans)
<!-- Fill this LAST, after the detailed plan below is written, so it summarizes the REAL plan. -->
<!-- Plain English for a non-engineer: NO file paths, NO todo numbers, NO wave/agent/tool names. -->

**What you'll get:** A grounded investigation path that proves where the local WebGL load is failing and fixes only the smallest thing that actually causes it. The end state is a Docker-hosted page that loads past the current failure point, plus a written evidence trail showing why the fix worked.

**Why this approach:** The current symptom can come from three different places that look similar from the browser: wrong static hosting headers, bad WebGL heap/startup sizing, or runtime work that is too heavy during boot. The plan separates those lanes before changing anything substantial, so you stop burning time on random rebuild settings.

**What it will NOT do:** It will not assume memory size is the only problem before headers and startup phases are checked.
It will not churn through multiple speculative WebGL settings at once.
It will not broaden into unrelated multiplayer or backend work.

**Effort:** Medium
**Risk:** Medium - the likely fix is small, but the root cause may sit in either hosting, build config, or runtime startup
**Decisions to sanity-check:** Primary browser target is a local Chromium-class browser; no new tests unless the winning fix exposes a stable regression boundary; server-side changes stay narrowly scoped to WebGL startup/connectivity impact.

Your next move: start work from this plan now, or ask for a high-accuracy review first. Full execution detail follows below.

---

> TL;DR (machine): Medium effort, medium risk, evidence-first plan to isolate Docker WebGL load failure across hosting, memory sizing, and runtime startup, then apply the smallest proven fix.

## Scope
### Must have
- Reproduce the WebGL load failure from the actual Docker-hosted webpage at `http://localhost:8085` and capture exact browser/network evidence.
- Verify that nginx serves the generated Unity artifacts with the correct `Content-Type`, `Content-Encoding`, caching, COOP/COEP, and websocket proxy behavior.
- Determine whether the failure occurs during loader fetch, wasm/data instantiation, Unity startup, or post-start runtime/network initialization.
- Run a controlled rebuild matrix around WebGL memory settings and startup behavior to isolate the smallest successful change set.
- If code or config changes are required, implement only the minimal fix that is proven by comparison evidence and then re-verify through the same browser/container surface.
### Must NOT have (guardrails, anti-slop, scope boundaries)
- Do not treat “memory out of bounds” as heap-size-only until hosting headers and runtime startup phase are ruled out.
- Do not make multiple speculative WebGL setting changes in one comparison build; preserve one-variable-at-a-time evidence.
- Do not replace Docker/nginx hosting or redesign multiplayer architecture unless the existing path is directly proven unworkable.
- Do not broaden into unrelated NPC dialogue, auth, Qdrant, or Cognee improvements.

## Verification strategy
> Zero human intervention - all verification is agent-executed.
- Test decision: none + agent-executed runtime verification through Docker, browser evidence capture, Unity build logs, and existing smoke scripts
- Evidence: .omo/evidence/task-<N>-unity-docker-webgl-memory-analysis-plan.<ext>

## Execution strategy
### Parallel execution waves
> Target 5-8 todos per wave. Fewer than 3 (except the final) means you under-split.
- Wave 1 establishes observable truth without edits: container state, generated artifact inventory, nginx response headers, browser console/network failure point, and dedicated-server readiness.
- Wave 2 isolates root cause with controlled comparison builds and runtime-phase checks: one hosting/control experiment, one memory-sizing experiment, one startup-deferral experiment, and one transport-path validation.
- Wave 3 applies the minimal proven fix and re-runs the full browser/container verification path.

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| 1 | none | 3, 4, 5 | 2 |
| 2 | none | 5 | 1 |
| 3 | 1 | 6 | 4 |
| 4 | 1 | 6 | 3 |
| 5 | 1, 2 | 6 | none |
| 6 | 3, 4, 5 | F1-F4 | none |

## Todos
> Implementation + Test = ONE todo. Never separate.
<!-- APPEND TASK BATCHES BELOW THIS LINE WITH edit/apply_patch - never rewrite the headers above. -->
- [ ] 1. Capture the failing browser and container baseline
  What to do / Must NOT do: Start or inspect the current Docker stack, open the actual local webpage, and capture the exact failure surface: console errors, network waterfall, request/response headers for `index.html`, `loader.js`, `.wasm`, `.data`, and the first websocket attempt if it occurs. Must NOT change build settings or source code in this todo.
  Parallelization: Wave 1 | Blocked by: none | Blocks: 3, 4, 5
  References (executor has NO interview context - be exhaustive): docker/docker-compose.yml:27-37; docker/nginx.conf:32-257; Assets/Editor/NPCDialogueBuild.cs:59-75
  Acceptance criteria (agent-executable): Evidence clearly states which phase fails first: asset fetch, wasm/data instantiation, Unity boot, or post-boot network path; saved artifact includes response headers and exact browser error text.
  QA scenarios (name the exact tool + invocation): happy: `docker compose -f docker/docker-compose.yml up -d`, `docker ps`, `curl -I http://localhost:8085/`, `curl -I http://localhost:8085/Build/LinuxWebGLWS.wasm.br`, browser/devtools or equivalent console+network capture against `http://localhost:8085`; failure: if page does not open or assets 404/incorrectly encode, capture nginx logs with `docker logs npc-webgl-client`. Evidence .omo/evidence/task-1-unity-docker-webgl-memory-analysis-plan.md
  Commit: N | none

- [ ] 2. Prove the dedicated-server and websocket side is or is not a contributor
  What to do / Must NOT do: Validate that the dedicated server container is alive, bound, and exposing the expected startup logs, then confirm whether the WebGL client even reaches the `/ws` proxy path during the failing run. Must NOT attribute a page-load failure to server transport without evidence that the page progressed far enough to attempt websocket connection.
  Parallelization: Wave 1 | Blocked by: none | Blocks: 5
  References (executor has NO interview context - be exhaustive): docker/docker-compose.yml:2-25; docker/nginx.conf:62-74; Tests/Integration/run-dedicated-server-smoke.sh:4-24
  Acceptance criteria (agent-executable): Either (a) server smoke passes and `/ws` is never reached before the memory failure, or (b) `/ws` evidence shows a transport/path issue worth keeping in scope.
  QA scenarios (name the exact tool + invocation): happy: `bash Tests/Integration/run-dedicated-server-smoke.sh`, `docker logs npc-dedicated-server --since 10m`, inspect nginx access log entries for `/ws`; failure: if the smoke fails, capture the failing condition and keep it separated from the page-load root cause. Evidence .omo/evidence/task-2-unity-docker-webgl-memory-analysis-plan.md
  Commit: N | none

- [ ] 3. Audit nginx/header behavior against the exact generated WebGL artifacts
  What to do / Must NOT do: Compare the mounted build output under `Builds/WebGL_client/LinuxWebGLWS` with the nginx rules that serve `.br`, `.gz`, `.unityweb`, `.wasm`, `.data`, and `/Build/` requests. Verify `Content-Type`, `Content-Encoding`, and cache/cross-origin headers on the actual files the current `index.html` references. Must NOT change Unity memory settings in this todo.
  Parallelization: Wave 2 | Blocked by: 1 | Blocks: 6
  References (executor has NO interview context - be exhaustive): docker/docker-compose.yml:34-36; docker/nginx.conf:76-257; Assets/Editor/NPCDialogueBuild.cs:63-75; ProjectSettings/ProjectSettings.asset:821-824
  Acceptance criteria (agent-executable): Either confirm hosting is correct for the referenced `.br` files, or produce a minimal nginx/config mismatch with reproducible evidence and the exact fix surface.
  QA scenarios (name the exact tool + invocation): happy: `find Builds/WebGL_client/LinuxWebGLWS -maxdepth 3 -type f | sort`, `curl -I` for referenced assets, compare headers to Unity expectations; failure: show any mismatched MIME/encoding/404 behavior and correlate it with the browser error from task 1. Evidence .omo/evidence/task-3-unity-docker-webgl-memory-analysis-plan.md
  Commit: N | none

- [ ] 4. Run a controlled WebGL memory and compression comparison matrix
  What to do / Must NOT do: Create comparison builds that vary one factor at a time: raise maximum memory, increase initial memory if needed, and use one compression/control variant only as a diagnostic baseline if hosting evidence remains ambiguous. Must NOT mix multiple independent toggles in the same comparison row unless a prior row already proved they are individually insufficient.
  Parallelization: Wave 2 | Blocked by: 1 | Blocks: 6
  References (executor has NO interview context - be exhaustive): Assets/Editor/NPCDialogueBuild.cs:59-75; ProjectSettings/ProjectSettings.asset:820-829; Assets/Settings/Build Profiles/WebGL - Desktop - Development.asset
  Acceptance criteria (agent-executable): Comparison matrix records, for each build, the changed setting, build success, artifact sizes, browser result, and whether the memory error moved, disappeared, or remained identical.
  QA scenarios (name the exact tool + invocation): happy: rebuild via Unity batch mode using `NPCDialogueBuild.PerformWebGLBuild`, inspect resulting `Build/` sizes and re-run task-1 browser checks per variant; failure: if builds fail or the symptom is unchanged across settings, record that memory tuning alone is not the root cause. Evidence .omo/evidence/task-4-unity-docker-webgl-memory-analysis-plan.md
  Commit: N | none

- [ ] 5. Isolate runtime startup pressure and WebGL-specific code paths
  What to do / Must NOT do: Inspect and, if needed, temporarily gate startup work so the page can reach first usable frame with minimal early allocations: dialogue initialization, backend probing, network bridge startup, and any WebGL-specific preservation path. Must NOT remove the AOT preservation workaround unless evidence shows it is directly harmful and a safer replacement exists.
  Parallelization: Wave 2 | Blocked by: 1, 2 | Blocks: 6
  References (executor has NO interview context - be exhaustive): Assets/Scripts/Runtime/Initialization/NPCSceneInitializationController.cs:44-52; Assets/Scripts/Runtime/Initialization/NPCSceneInitializationController.cs:134-199; Assets/Scripts/Runtime/Initialization/NPCBackendReadinessService.cs:42-48; Assets/Scripts/Runtime/Initialization/NPCBackendReadinessService.cs:99-183; Assets/Scripts/Runtime/NPCDialogue/AOTGenericPreservation.cs:13-17
  Acceptance criteria (agent-executable): Evidence shows whether deferred startup changes the failure point or memory behavior; if it does, the exact heavy phase is identified.
  QA scenarios (name the exact tool + invocation): happy: apply a minimal deferred-start experiment, rebuild WebGL, and compare browser behavior to task 1; failure: if no behavioral change occurs, record startup pressure as ruled out and keep the evidence. Evidence .omo/evidence/task-5-unity-docker-webgl-memory-analysis-plan.md
  Commit: N | none

- [ ] 6. Apply the smallest proven fix and verify through the real hosting surface
  What to do / Must NOT do: Implement only the minimal fix supported by tasks 1-5, which may be nginx header/config changes, WebGL memory configuration changes, startup deferral changes, or a tightly scoped combination. Rebuild/restart as needed and verify the page now loads correctly on the Docker-hosted webpage. Must NOT leave behind temporary diagnostics or experimental toggles that were not part of the winning fix.
  Parallelization: Wave 3 | Blocked by: 3, 4, 5 | Blocks: F1-F4
  References (executor has NO interview context - be exhaustive): docker/docker-compose.yml:27-37; docker/nginx.conf:32-257; Assets/Editor/NPCDialogueBuild.cs:59-75; ProjectSettings/ProjectSettings.asset:820-829; Assets/Scripts/Runtime/Initialization/NPCSceneInitializationController.cs:44-52; Assets/Scripts/Runtime/Initialization/NPCBackendReadinessService.cs:99-183
  Acceptance criteria (agent-executable): The Docker-hosted page loads past the prior failure point and the original memory error no longer appears; any remaining warnings are documented and non-blocking.
  QA scenarios (name the exact tool + invocation): happy: rebuild/restart stack, load `http://localhost:8085`, confirm Unity instance reaches usable state and, if applicable, websocket path works; failure: if the first fix is insufficient, compare against task evidence and iterate only within the proven root-cause lane. Evidence .omo/evidence/task-6-unity-docker-webgl-memory-analysis-plan.md
  Commit: Y | fix(webgl): resolve Docker-hosted WebGL load failure

## Final verification wave
> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.
- [ ] F1. Plan compliance audit
  Verify the executed fix matches the winning evidence lane from tasks 1-5 and did not silently broaden scope.
- [ ] F2. Code quality review
  Review changed files for unnecessary toggles, debug residue, over-broad conditionals, or hidden regressions in server/client startup behavior.
- [ ] F3. Real manual QA
  Re-run the Docker-hosted webpage load, asset header checks, and dedicated-server smoke on the final build/config state.
- [ ] F4. Scope fidelity
  Confirm the delivered result solves the WebGL load blocker without claiming unrelated multiplayer or backend issues were solved.

## Commit strategy
- No commit during evidence-gathering todos 1-5.
- Create a single final commit only if task 6 changes tracked files and the winning fix is verified through the Docker-hosted webpage.
- Commit message target: `fix(webgl): resolve Docker-hosted WebGL load failure`.

## Success criteria
- A written evidence trail exists for the current failing state, including exact browser error text and response headers for the relevant Unity assets.
- The root cause is narrowed to one primary lane: hosting/header mismatch, WebGL heap sizing, runtime startup pressure, WebGL-specific IL2CPP issue, or a tightly scoped combination.
- The smallest proven fix is implemented and verified from `http://localhost:8085` through the real Docker/nginx hosting path.
- The dedicated server smoke still passes or any independent server issue is explicitly separated from the original webpage load failure.
- The final state does not rely on unexplained “works with random settings” behavior; the plan execution leaves a causal explanation for why the fix worked.
