# EditorAttributes Pattern Research

_Last verified: 2026-07-01_

## Installed Package

- Package: `com.v0lt.editor-attributes`
- Version: `3.0.4`
- Source: `Packages/com.v0lt.editor-attributes/`
- Runtime assembly: `EditorAttributes`
- Runtime asmdef: `Packages/com.v0lt.editor-attributes/Runtime/Scripts/EditorAttributes.asmdef`
- Local documentation: `Packages/com.v0lt.editor-attributes/Documentation~/EditorAttributesDocumentation.rtf`
- Local samples: `Assets/Samples/EditorAttributes/3.0.4/AttributesSamples/`

The package adds Inspector attributes for Unity components and ScriptableObjects so we can improve configuration UX without writing custom editors. For this project, use it as an editor-safety layer around LLMUnity, direct LocalAI, Qdrant, Cognee, RAG, NPC profiles, and diagnostic controls.

## Assembly Notes

Project runtime code should explicitly reference `EditorAttributes` from `Assets/Scripts/Runtime/NPCSystem.Runtime.asmdef` before adding attributes to runtime MonoBehaviours or ScriptableObjects. The package is auto-referenced, but explicit asmdef references keep intent clear and avoid per-assembly resolution surprises.

## High-Value Attribute Matrix

| Attribute | Source | Target | Project Usage |
|---|---|---|---|
| `Required` | `Runtime/Scripts/Attributes/MiscellaneousAttributes/RequiredAttribute.cs` | fields | Highlight required scene references (`LLM`, `LLMAgent`, `RAG`, profiles, services). Supports `ReferenceFixMode.Self` and custom provider methods. |
| `Validate` | `MiscellaneousAttributes/ValidateAttribute.cs` | fields | Validate ports, URLs, model names, collection names, token limits, file paths, and profile slugs. Condition method can return `bool` or `ValidationCheck`. |
| `Button` | `ButtonAttributes/ButtonAttribute.cs` | methods | Add explicit Inspector actions: auto-assign references, log config, validate endpoints, normalize paths. Buttons can be conditional and can mark objects dirty. |
| `InlineButton` | `ButtonAttributes/InlineButtonAttribute.cs` | fields | Place a small action next to a field, e.g. normalize a URL or copy endpoint preview. |
| `OnValueChanged` | `MiscellaneousAttributes/OnValueChangedAttribute.cs` | fields | Normalize paths/slugs and update derived preview/status fields when a value changes. Keep methods safe and local. |
| `ShowField` / `HideField` / `EnableField` / `DisableField` | `ConditionalAttributes/*` | fields | Show or enable LocalAI/Qdrant/Cognee/remote embedder settings only when their toggles are enabled. |
| `FoldoutGroup` / `TabGroup` / `ToggleGroup` / layout groups | `GroupingAttributes/*` | fields | Group complex components into LLM references, LocalAI, RAG, Qdrant, Cognee, profiles, history, events, diagnostics. |
| `ReadOnly` | `MiscellaneousAttributes/ReadOnlyAttribute.cs` | fields | Display cached editor diagnostics without allowing accidental edits. |
| `ShowInInspector` | `MiscellaneousAttributes/ShowInInspectorAttribute.cs` | properties/members | Show computed read-only state such as active endpoint preview and resolved NPC paths. |
| `HelpBox`, `Title`, `Line`, `GUIColor` | `DecorativeAttributes/*` | fields | Add local architecture notes and visual grouping directly to the Inspector. |
| `FilePath`, `FolderPath` | `MiscellaneousAttributes/*PathAttribute.cs` | string fields | Improve StreamingAssets knowledge paths, `.rag` paths, and output/log paths. |
| Dropdown attributes | `DropdownAttributes/*` | fields | Prefer safe choices for scenes, tags, layers, types, properties, and animator params when a free-text field is too error-prone. |
| Numeric helpers (`Clamp`, `ProgressBar`, `UnitField`, etc.) | `NumericalAttributes/*` | numeric fields | Make token limits, timeouts, confidence values, and tuning parameters easier to understand. |

## Syntax Examples From Installed Source/Samples

```csharp
using EditorAttributes;
using UnityEngine;

[SerializeField, Required]
private GameObject objectField;

[SerializeField, Required(fixMode: ReferenceFixMode.Self)]
private Collider colliderField;

[SerializeField, Required(nameof(GetAudioReference))]
private AudioSource audioField;
private Object GetAudioReference() => GetComponent<AudioSource>();
```

```csharp
[SerializeField, Validate("Port must be between 1 and 65535", nameof(IsValidPort))]
private int remotePort = 8080;
private bool IsValidPort() => remotePort > 0 && remotePort <= 65535;
```

```csharp
[SerializeField, Validate(nameof(ValidateEndpoint), applyToCollection: false)]
private string endpoint;
private ValidationCheck ValidateEndpoint()
{
    return endpoint.StartsWith("http://") || endpoint.StartsWith("https://")
        ? ValidationCheck.Pass()
        : ValidationCheck.Fail("Endpoint must start with http:// or https://", MessageMode.Warning);
}
```

```csharp
[Button("Auto Assign Scene References")]
private void AutoAssignSceneReferences() { }

[SerializeField, InlineButton(nameof(NormalizePath), "Normalize", 80f)]
private string knowledgeSourcePath;

[SerializeField, OnValueChanged(nameof(NormalizeSlug))]
private string npcSlug;

[SerializeField, ShowField(nameof(useQdrantRag))]
private QdrantRAGService qdrantRag;

[FoldoutGroup("LocalAI", true, nameof(remoteHost), nameof(remotePort), nameof(remoteModel))]
[SerializeField]
private bool localAiHeader;

[SerializeField, ReadOnly]
private string lastValidationStatus;

[ShowInInspector]
private string ActiveEndpoint => $"http://{remoteHost}:{remotePort}/v1";
```

## Project-Specific Patterns

### NPCProfile

Use EditorAttributes to make profile assets self-validating:

- Group identity, personality, behavior, gameplay actions, sampling, knowledge, LoRA, and history.
- Validate `npcSlug`, `maxTokens`, `ragResults`, paths, and derived defaults.
- Use buttons for `NormalizeProfilePaths` and `ValidateProfileForInspector`.
- Show resolved slug, knowledge path, LoRA path, and history file as computed Inspector values.

### QdrantRAGService

Use EditorAttributes to prevent endpoint/collection mistakes:

- Validate `qdrantUrl` and `collectionName`.
- Show computed search endpoint.
- Add buttons that only log/validate/copy endpoint details; avoid automatic network calls in `OnValidate`.

### NPCDialogueManager

Use EditorAttributes after smaller components prove the pattern:

- Group the large component into references, direct LocalAI, local RAG, Qdrant, Cognee, profiles, history, and diagnostics.
- Conditional-display optional subsystem fields.
- Add explicit diagnostic buttons for auto-assign and configuration logging.
- Keep direct LocalAI runtime behavior unchanged.

### EditorWindows

EditorAttributes does not automatically replace manual IMGUI code in `EditorWindow.OnGUI`. For `NPCFactoryWindow`, prefer a separate `ScriptableObject` settings asset if we want attribute-driven fields later.

## Safe Usage Rules

1. Use `nameof(...)` for method/field references in attributes.
2. Keep validation methods side-effect free.
3. Do not perform network calls from `OnValidate` or `OnValueChanged`; use explicit `[Button]` methods.
4. Start with warning-level validation. Promote to build-killing validation only after scene defaults are clean.
5. Do not modify vendored `Assets/LLMUnity/*` code unless the task explicitly asks for it.
6. Compile after each production component pass.
7. Use GladeKit to verify live scene wiring after runtime component changes.

## First Implementation Targets

1. `NPCProfile`: low-risk ScriptableObject asset UX.
2. `QdrantRAGService`: endpoint/collection safety.
3. `NPCInspectorDiagnosticsPilot`: pilot diagnostics control surface for package behavior.
4. `NPCDialogueManager`: defer heavy annotation until pilot and smaller targets compile cleanly.

## Verification Checklist

- [ ] `NPCSystem.Runtime.asmdef` references `EditorAttributes` if runtime files use these attributes.
- [ ] Unity compile succeeds after every C# change.
- [ ] Existing scene values and profile assets remain serialized.
- [ ] No network call runs automatically in edit mode.
- [ ] Inspector buttons are explicit, safe, and idempotent.
- [ ] Optional subsystem fields are gated by their toggles where possible.
