# GladeKit UI Workflow

**Last verified:** 2026-07-01  
**Project:** `Unity_Linux_LLM`

This document describes the working GladeKit MCP workflow for reliable Unity UI creation and debugging in this project.

## Why this exists

The previous UI path was failing in predictable ways:

- incomplete dropdown and input field hierarchies
- missing control references such as `template`, `textComponent`, `placeholder`, `fillRect`, and `handleRect`
- event handlers wired for legacy controls but not TMP controls
- EventSystem creation that did not match the project's active input stack

The project uses the new Input System, so UI creation must respect that.

## Project facts that matter

- `unity://project/info` reports `inputSystem = NEW`
- the active scene contains mixed legacy and TMP UI
- the EventSystem in the current scene uses `InputSystemUIInputModule`

Implication:

- new UI work must validate input module choice
- tools and skills must support both legacy UGUI controls and TMP-based controls

## Recommended creation sequence

1. Call `list_ui_hierarchy` if editing an existing UI.
2. Call `create_canvas` if no canvas exists.
3. Call `create_event_system` if no EventSystem exists.
4. Create panels and layout parents first.
5. Create controls with `create_ui_element`.
6. Refine size, anchoring, text, options, and behavior with `set_ui_properties`.
7. Wire persistent interactions with `set_ui_event`.
8. Validate with:
   - `check_ui_element_exists`
   - `get_ui_element_info`
   - `get_ui_event_handlers`

## Minimum validation checklist by control

### Button

- `Button` exists
- `Image` exists
- visible text child exists
- `onClick` is present when required

### Toggle

- `Toggle` exists
- `graphic` assigned
- `targetGraphic` assigned

### Slider

- `Slider` exists
- `fillRect` assigned
- `handleRect` assigned

### Dropdown / TMP_Dropdown

- dropdown exists
- `template` assigned
- `captionText` assigned
- `itemText` assigned
- option list populated

### InputField / TMP_InputField

- input field exists
- `textComponent` assigned
- `placeholder` assigned when expected
- line/content type set correctly

### ScrollView

- `ScrollRect` exists
- `viewport` assigned
- `content` assigned

## Debugging order

When UI is broken, inspect in this order:

1. `list_ui_hierarchy`
2. `check_ui_element_exists`
3. `get_ui_element_info`
4. `get_ui_event_handlers`
5. `get_gameobject_components` or `get_component_inspector_properties` for deeper inspection

## Skills added for this workflow

- `.agents/skills/unity-ui-builder/`
- `.agents/skills/unity-ui-debugger/`

These skills encode the required creation order, validation steps, and repair strategy so future UI work does not regress back to partial or non-functional controls.
