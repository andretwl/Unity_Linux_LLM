# Scene Initialization Workflow

**Last verified:** 2026-07-01 13:54 -03  
**Scene:** `Assets/Scenes/NPCDialoguePrototype.unity`  
**GladeKit bridge:** `0.7.12`  
**LocalAI endpoint:** `http://localhost:8080`

This scene now has an explicit initialization layer. Future agents should treat `NPCSceneInitialization` as the startup coordinator and should inspect it with GladeKit before changing scene startup, networking, dialogue, UI, or spawning behavior.

## Root scene objects that matter

The active prototype scene root contains:

1. `NPCFlowLogger`
2. `NPCDialoguePrototypeNetwork`
3. `NPCDialogueSystem`
4. `NPCDialogueRuntimeBridge`
5. `NPCDialogueUI`
6. `NPCSceneInitialization`

The visible Game view captured through GladeKit currently shows the camera skybox/horizon and not the overlay UI. The Canvas is active and configured as `Screen Space - Overlay`, so this may be a limitation of the camera-render capture path rather than a missing Canvas. Always verify UI through both Game view and Inspector before making visual conclusions.

## Deterministic script execution order

Runtime initialization scripts use `DefaultExecutionOrder` so Unity startup is deterministic:

| Order | Script | Role |
|---:|---|---|
| `-3000` | `NPCFlowLogger` | Create/resolve flow logger before any bootstrap logging. |
| `-2500` | `NPCNetworkBootstrap` | Resolve `NetworkManager`, `UnityTransport`, and player prefab; apply transport config. |
| `-2000` | `NPCSceneInitializationController` | Run the explicit startup phases below. |
| `-1500` | `NPCDialogueManager` | Begin idempotent dialogue initialization. |
| `-1000` | `NPCDialogueBootstrapper` | Existing dialogue bootstrap/default-selection hook. |
| `-900` | `NPCDialogueNetworkBridge` | Resolve server-authoritative bridge/session references. |
| `-400` | `NPCDialogueUIController` | Bind UI after services and bridge have begun initialization. |
| `500` | `NPCDialogueSmokeValidator` | Validate after the scene has had a chance to initialize. |

The order is protected by `Assets/Scripts/Tests/Editor/NPCSceneInitializationTests.cs`.

## `NPCSceneInitialization` GameObject

Components:

- `Transform`
- `NPCSceneInitializationController`

Inspector references verified through GladeKit:

- `flowLogger` -> `NPCFlowLogger`
- `networkBootstrap` -> `NPCDialoguePrototypeNetwork`
- `dialogueManager` -> `NPCDialogueSystem`
- `networkBridge` -> `NPCDialogueRuntimeBridge`
- `smokeValidator` -> `NPCDialogueSystem`

Startup toggles:

- `initializeOnStart = true`
- `configureNetworkTransport = true`
- `initializeDialogueManager = true`
- `initializeNetworkBridge = true`
- `validateAfterInitialization = true`
- `startNetworkingAfterInitialization = false`

`startNetworkingAfterInitialization` is intentionally false. The scene is configured and validated on start, but it does not automatically start host/server/client mode unless we explicitly opt in for a test or build profile.

## Step-by-step startup phases

`NPCSceneInitializationController.OrderedPhases` runs:

1. **Logger**
   - Resolves or creates `NPCFlowLogger`.
   - Ensures all later phases can emit structured JSONL/console flow events.
2. **SceneReferences**
   - Resolves `NPCNetworkBootstrap`, `NPCDialogueManager`, `NPCDialogueNetworkBridge`, and `NPCDialogueSmokeValidator`.
   - This catches missing scene references early.
3. **NetworkTransport**
   - Calls `NPCNetworkBootstrap.ApplyTransportConfiguration()`.
   - Ensures `NetworkManager.NetworkConfig.NetworkTransport`, player prefab, WebSocket flag, address, port, listen address, and WebSocket path are applied before any network start.
4. **DialogueServices**
   - Calls `NPCDialogueManager.InitializeAsync()`.
   - Builds profile index, loads/repairs history, and prepares local RAG state.
5. **NetworkBridge**
   - Calls `NPCDialogueNetworkBridge.InitializeAsync()`.
   - Captures baseline per-client history/evidence state and updates local notebook state.
6. **Validation**
   - Calls `NPCDialogueSmokeValidator.ValidateConfiguration()`.
   - Confirms the configured `LLM`, `LLMRAG`, `LLMAgent`, and `RAG` references match the dialogue manager.
7. **Spawning**
   - If `startNetworkingAfterInitialization` is true, calls `NPCNetworkBootstrap.StartConfiguredMode()`.
   - Currently off by default to avoid accidental host/client startup in edit/play iteration.

## Network player prefab

`NPCNetworkBootstrap.playerPrefabResourcesPath` points at `Resources/Networking/NPCPlayerAvatar`, which resolves to:

- `Assets/Resources/Networking/NPCPlayerAvatar.prefab`

The prefab is the operational Netcode player object spawned by `NetworkManager` when host/server/client mode is started. Verified root components:

- `NetworkObject`
- `NPCPlayerNetworkAvatar`
- `CharacterController`
- `Animator`
- `NetworkAnimator` with owner authority
- `NPCOwnerNetworkTransform` with owner-authoritative transform sync
- `NPCNetworkPlayerController`
- `PlayerInput`

The visible avatar is the child `AvatarVisual`, a blue URP capsule using `Assets/Materials/NetworkPlayerBlue.mat`. The root object stays at the CharacterController feet (`0,0,0`) and the visual capsule is offset to `0,1,0` so the controller bottom rests on the ground.

The controller uses the `Player` action map from `Assets/InputSystem_Actions.inputactions`:

- `Move` (`Vector2`) — WASD / arrows / left stick
- `Look` (`Vector2`) — mouse/gamepad look, used for owner camera yaw
- `Jump` (`Button`) — CharacterController jump
- `Sprint` (`Button`) — sprint speed modifier

The player controller is owner-only: the owning client reads input and moves its `CharacterController`; `NPCOwnerNetworkTransform` replicates the resulting transform; `NetworkAnimator` replicates animator parameters/triggers.

## Folder and namespace structure

Runtime scripts are organized under coherent feature folders:

```text
Assets/Scripts/Runtime/
├── Initialization/
│   └── NPCSceneInitializationController.cs
├── Networking/
│   ├── NPCDialogueMessageModels.cs
│   ├── NPCDialogueNetworkBridge.cs
│   ├── NPCNetworkBootstrap.cs
│   ├── NPCNetworkSessionManager.cs
│   ├── NPCPlayerNetworkAvatar.cs
│   └── NPCTransportConfig.cs
├── NPCDialogue/
│   ├── NPCDialogueManager.cs
│   ├── NPCDialogueBootstrapper.cs
│   ├── NPCDialogueSmokeValidator.cs
│   ├── NPCDialogueUIController.cs
│   ├── NotebookUIController.cs
│   ├── NPCEvidenceState.cs
│   ├── NPCHistoryStore.cs
│   ├── NPCProfile.cs
│   ├── NPCProfilePromptComposer.cs
│   ├── NPCRAGImporter.cs
│   ├── NPCRAGMetadata.cs
│   ├── QdrantRAGService.cs
│   └── Logging/
└── Samples/RAG/
    └── QdrantGroundedLLMSample.cs
```

Namespace audit result:

- Runtime scripts: `NPCSystem`
- Editor tests: `NPCSystem.Tests`
- Editor tools: `NPCSystem.Editor` or `GladeAgenticAI.Core.Tools.Implementations.NPC`
- Issues found after cleanup: `0`

## Verification commands / tools

Use these after future startup changes:

```bash
python3 - <<'PY'
from pathlib import Path
import re
files=sorted(Path('Assets/Scripts').rglob('*.cs'))
issues=[]
for p in files:
    text=p.read_text(errors='replace')
    ns=re.search(r'namespace\s+([A-Za-z0-9_.]+)', text)
    namespace=ns.group(1) if ns else ''
    s=str(p)
    if '/Tests/' in s or s.startswith('Assets/Scripts/Tests/'):
        region='Tests'
    elif '/Editor/' in s or s.startswith('Assets/Scripts/Editor/'):
        region='Editor'
    elif '/Runtime/' in s or s.startswith('Assets/Scripts/Runtime/'):
        region='Runtime'
    else:
        region='Other'
    if region=='Runtime' and namespace != 'NPCSystem':
        issues.append((s,'runtime namespace not NPCSystem',namespace))
    if region=='Tests' and namespace != 'NPCSystem.Tests':
        issues.append((s,'test namespace not NPCSystem.Tests',namespace))
    if region=='Editor' and namespace not in ('NPCSystem.Editor','GladeAgenticAI.Core.Tools.Implementations.NPC'):
        issues.append((s,'editor namespace unexpected',namespace))
print('script_count', len(files))
print('issues_count', len(issues))
for issue in issues:
    print('ISSUE | ' + ' | '.join(issue))
PY
```

GladeKit checks:

- `compile_scripts` must report `hasErrors=false`.
- `get_scene_hierarchy(rootOnly=true)` must include `NPCSceneInitialization`.
- `get_component_inspector_properties` on `NPCSceneInitialization/NPCSceneInitializationController` must show all five object references assigned.
- `get_unity_console_logs` should show no compile/runtime errors.

LocalAI checks:

- `GET http://localhost:8080/readyz` -> HTTP 200
- `GET http://localhost:8080/healthz` -> HTTP 200
- `GET http://localhost:8080/v1/models` includes `llama-3.1-8b-q4-k-m` and `nomic-embed-text-v1.5`.
