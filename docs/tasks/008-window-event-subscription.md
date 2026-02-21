# Task 008: Window Event Subscription System

**Priority:** 3 (Core Infrastructure)
**Depends on:** 002
**Blocks:** 009, 010, 015, 017

## Objective

Implement a centralized window event subscription system using `SetWinEventHook` that distributes window lifecycle and geometry events to all Harbor subsystems (overlay tracking, dock updates, foreground monitoring).

## Technical Reference

Refer to `docs/Design.md` Sections 4A Step 1 (Event Subscription), 3B (EVENT_OBJECT_CREATE/DESTROY), 6B (fullscreen detection), and 7A–7B (overlay synchronization) for event usage.

## Requirements

1. **Event Hook Manager:**
   - Create a `WindowEventManager` class in `Harbor.Core`
   - Subscribe to the following events via `SetWinEventHook`:
     - `EVENT_SYSTEM_FOREGROUND` — foreground window changed
     - `EVENT_OBJECT_LOCATIONCHANGE` — window moved or resized
     - `EVENT_OBJECT_CREATE` — new window created
     - `EVENT_OBJECT_DESTROY` — window destroyed
   - Use out-of-context hooks (WINEVENT_OUTOFCONTEXT) to avoid DLL injection

2. **Event Distribution:**
   - Implement a publish-subscribe pattern: components register interest in specific event types
   - Events are dispatched on a dedicated thread (not the UI thread) to avoid blocking
   - Provide optional UI-thread marshaling for subscribers that need it

3. **Event Filtering:**
   - Filter out events from Harbor's own windows (avoid self-tracking)
   - Filter out invisible windows, tool windows, and other non-top-level windows
   - Provide HWND-based filtering so subscribers can track specific windows

4. **Lifecycle Management:**
   - Hooks are registered on startup and unregistered on shutdown
   - Handle thread affinity requirements of `SetWinEventHook` (hooks must be unhooked from the same thread)

## Acceptance Criteria / Tests

- [ ] `EVENT_SYSTEM_FOREGROUND` fires when switching between applications (verified by logging)
- [ ] `EVENT_OBJECT_LOCATIONCHANGE` fires when dragging a window (verified by logging)
- [ ] `EVENT_OBJECT_CREATE` fires when opening a new application
- [ ] `EVENT_OBJECT_DESTROY` fires when closing an application
- [ ] Multiple subscribers can register for the same event type and all receive callbacks
- [ ] Events from Harbor's own windows are filtered out
- [ ] Clean shutdown unhooks all event hooks without errors
- [ ] Unit tests verify subscriber registration, event dispatch, and filtering logic using mock events
