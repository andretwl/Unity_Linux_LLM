---
name: unity-ui-builder
description: Use when creating or extending Unity UGUI or TextMeshPro UI through GladeKit MCP. Covers canvas setup, EventSystem setup, buttons, toggles, sliders, scroll views, dropdowns, input fields, auth/login-register UI, backend wiring patterns, and post-create validation.
---

# Unity UI Builder

Use this skill when the user wants new Unity UI created or existing UI extended through GladeKit MCP tools.

## Goal

Create UI that is actually usable at runtime:

- visible
- positioned predictably
- wired to the correct input stack
- complete enough for Unity's built-in controls to function
- validated after creation instead of assumed correct

## Required Preflight

1. Read `unity://project/info`.
2. If no canvas exists, call `create_canvas`.
3. If no EventSystem exists, call `create_event_system`.
4. If working in an existing scene, call `list_ui_hierarchy` before editing.

## Input Rule

Match the project's active input stack.

- If the project uses the new Input System, the EventSystem must use `InputSystemUIInputModule`.
- Do not assume `StandaloneInputModule` is correct.

## Creation Rule

Prefer canonical Unity controls over ad-hoc empty GameObjects with manually attached components.

- Use `create_ui_element` for controls.
- Use `set_ui_properties` after creation for size, anchoring, text, options, colors, and content behavior.
- Use `set_layout_group_properties` for containers.
- Use `set_ui_event` for persistent button, dropdown, toggle, slider, and input events.

## Auth / Postgres Rule

When the user asks for login, register, account creation, or session UI backed by PostgreSQL:

- Do NOT wire the Unity client directly to PostgreSQL unless the user explicitly asks for a trusted local/admin-only tool.
- Default to a backend auth API in front of PostgreSQL.
- In this repo, prefer the established runtime pattern: `UnityWebRequest` + JSON request/response DTOs.
- Treat PostgreSQL as server-side storage for users, password hashes, refresh tokens, and session records.
- Treat Unity as a client that sends auth requests and stores only client-safe session state.

The safe default architecture is:

1. Unity auth UI
2. Unity auth/session controller script
3. HTTP auth service
4. PostgreSQL database

Never put raw SQL credentials, database hostnames, password hashing logic, or direct database writes in button handlers.

## Auth Flow Pattern

For login/register UI, prefer this split:

- `AuthUIController`: mode switching, local validation, error display, loading state, submit dispatch
- `AuthService` or similarly named runtime service: `UnityWebRequest` calls, request serialization, response parsing
- `SessionState` or similarly named runtime component/service: current user id, display name, auth token, expiration, remember-me state

Keep the UI controller thin. It should validate fields, call the auth service, and update session state on success.

## Session Rule

When the user asks to set session info after login:

- Store only client-safe values locally: auth token or session token, user id, display name, expiry, remember-me flag
- Do not store plaintext passwords after submit
- Clear sensitive input fields after a successful submit or explicit logout
- If persistent login is requested, persist the minimum needed session token and expiry, then restore through a session bootstrap step
- Prefer a dedicated session object/service over scattering session fields across UI widgets

If the repo already has a session-style manager, follow that pattern instead of inventing a new one.

## Auth Validation Rule

For register/login forms, validate both UI structure and behavior.

- required fields present
- password fields use password content type
- confirm-password field is shown only in register mode
- remember-me toggle is shown only in login mode unless the user asks otherwise
- submit button disabled during in-flight request
- error text hidden by default and shown only on validation/request failure
- success path updates session state and UI mode/panel state as requested

## Backend Contract Rule

Before wiring auth UI to code, identify or create a clear contract for:

- register endpoint
- login endpoint
- logout endpoint if needed
- session restore / me endpoint if needed
- response payload fields required by the UI and session layer

If the backend does not exist yet, create stub service methods and keep the UI fully wired to those methods rather than leaving button events unattached.

## Canonical Order

1. `list_ui_hierarchy` when editing an existing UI.
2. `create_canvas` if needed.
3. `create_event_system` if needed.
4. For auth work, inspect existing scripts/services before creating new ones.
5. Create parent panels and layout containers first.
6. Create interactive controls under those containers.
7. Apply sizing and anchoring with `set_ui_properties`.
8. Wire events with `set_ui_event`.
9. Assign script references or ensure reliable runtime auto-resolution.
10. Validate with `get_ui_element_info`, `get_ui_event_handlers`, and `check_ui_element_exists`.

## Control-Specific Checks

After creating a control, validate these minimum requirements.

### Button

- `Button` component exists
- `Image` component exists
- visible text child exists
- `get_ui_event_handlers` shows the expected `onClick` target when wired

### Toggle

- `Toggle` component exists
- toggle `graphic` is assigned
- `targetGraphic` is assigned
- label exists and is readable

### Slider

- `Slider` component exists
- `fillRect` is assigned
- `handleRect` is assigned
- min, max, and value are correct

### Dropdown / TMP_Dropdown

- dropdown component exists
- template is assigned
- caption text is assigned
- item text is assigned
- options list is populated

### InputField / TMP_InputField

- input component exists
- text component is assigned
- placeholder is assigned when requested
- line type and content type match the requested behavior

For auth inputs also verify:

- username/email field type matches the intended credential
- password and confirm-password fields are masked
- submit/enter behavior targets the correct controller method

### ScrollView

- `ScrollRect` exists
- viewport is assigned
- content is assigned
- scrollbar references are present when expected

## Layout Rule

Do not stop after creating children.

- Set sizes explicitly for important controls.
- Set anchored positions explicitly unless the parent layout group controls placement.
- If using a layout group, validate the parent container rather than forcing child positions manually.

## Validation Sequence

Always validate with tools after creation.

1. `check_ui_element_exists`
2. `get_ui_element_info`
3. `get_ui_event_handlers` when events were wired
4. component inspector checks for assigned script references on the UI controller
5. script compile status when auth scripts were created or modified
6. `list_ui_hierarchy` when debugging nesting or missing children

For auth flows, the task is not done until:

- the UI mode switch works structurally
- submit buttons are persistently wired
- the controller references are assigned
- the session-success path is represented in code, even if the backend is still stubbed

## Failure Pattern To Avoid

Do not assume these are enough by themselves:

- `add_component(Button)` on an empty GameObject
- adding `Dropdown` or `InputField` without the required child references
- creating a canvas without checking the EventSystem
- wiring a button without verifying the persistent handler list
- calling PostgreSQL directly from Unity runtime UI code by default
- storing plaintext passwords in fields, PlayerPrefs, or long-lived MonoBehaviour state
- embedding database connection strings in client scripts

## Existing Scene Rule

When adding UI to this project, preserve the current scene's mixed legacy/TMP reality unless the user asked for a full migration.

- Some existing controls are legacy `InputField` and `Dropdown`.
- Some existing controls are TMP-based.
- Match the local surface you are extending unless there is a concrete reason to standardize it.

## Completion Standard

The task is not done when the controls exist in the hierarchy.

The task is done when:

- the hierarchy is correct
- required references are assigned
- text/options are present
- event handlers are wired
- the validation tools confirm the result
