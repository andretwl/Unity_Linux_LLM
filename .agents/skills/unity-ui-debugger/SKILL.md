---
name: unity-ui-debugger
description: Use when Unity UI created through GladeKit MCP is broken, invisible, missing references, misaligned, or has buttons, dropdowns, toggles, sliders, or input fields that do not function. Focuses on inspection-first debugging and repair.
---

# Unity UI Debugger

Use this skill when a Unity UI control exists but does not work correctly.

## Goal

Diagnose broken UI by inspecting live Unity state first, then repair the smallest missing piece.

## Debug Order

1. `list_ui_hierarchy`
2. `check_ui_element_exists`
3. `get_ui_element_info`
4. `get_ui_event_handlers` when interaction is broken
5. `get_gameobject_components` or `get_component_inspector_properties` if the UI-specific tools are not enough

Do not start by rewriting the whole UI.

## What To Look For

### Missing Runtime Interaction

Check:

- EventSystem exists
- EventSystem input module matches the project's input stack
- control is active
- `interactable` is true
- `CanvasGroup` is not blocking interaction unexpectedly
- event handlers are actually present

### Broken Button

Check:

- `Button` exists
- target graphic exists
- text is visible
- `get_ui_event_handlers` count is non-zero when it should be wired

### Broken Toggle

Check:

- `graphic` is assigned
- `targetGraphic` is assigned
- parent `CanvasGroup` is not disabling interaction

### Broken Slider

Check:

- `fillRect` exists
- `handleRect` exists
- min, max, and current value are valid

### Broken Dropdown

Check:

- template exists
- caption text exists
- item text exists
- option count is greater than zero

### Broken Input Field

Check:

- text component exists
- placeholder exists if expected
- line type and content type are correct
- submit/end-edit handlers are wired when required

### Broken Scroll View

Check:

- viewport exists
- content exists
- movement direction flags match expected behavior

## Repair Strategy

Prefer the smallest fix.

1. If a property is missing or wrong, use `set_ui_properties`.
2. If a persistent event is missing, use `set_ui_event`.
3. If stale handlers are wrong, use `remove_ui_event` and then rewire.
4. If the control is structurally incomplete, replace that specific control with a fresh canonical one rather than manually patching every child.

## When To Recreate Instead Of Patch

Recreate the control if any of these are true:

- dropdown/input field template references are largely missing
- slider has no fill and no handle references
- scroll view has no viewport and no content
- the hierarchy is so malformed that patching is slower and riskier than replacing

## Output Standard

When reporting the problem, state it concretely.

Good:

- `Canvas/AuthPanel/RememberToggle` has a `Toggle` but no assigned `graphic`, so it cannot show its checked state.
- `Canvas/TmpDropdown` exists, but its template reference is null, so it cannot expand correctly.

Avoid vague descriptions like `the layout is broken` unless you can name the missing or incorrect references.

## Completion Standard

Do not stop after editing.

Re-run the same inspection tools and confirm the repaired control now has:

- the required references
- the expected event handlers
- the expected options/text
- the expected active/interactable state
