# Task 003: ManagedShell Service Integration

**Priority:** 2 (Foundation)
**Depends on:** 001
**Blocks:** 004, 005, 006

## Objective

Initialize and configure ManagedShell's core services so that Harbor can register AppBars, enumerate running tasks, and host system tray icons. This task establishes the bridge between Harbor and ManagedShell's `ShellManager`.

## Technical Reference

Refer to `docs/Design.md` Sections 2 (Technical Stack), 2.1 (Dependency Risk), 3A (Top Menu), 3B (Dock), and 5E (System Tray Icons) for ManagedShell usage context.

## Requirements

1. Create a `ShellServices` class (or similar) in `Harbor.Core` that:
   - Initializes ManagedShell's `ShellManager` with required configuration
   - Exposes `TaskService` for dock/task enumeration (Section 3B)
   - Exposes `TrayService` for system tray icon hosting (Section 3A)
   - Handles clean shutdown and resource disposal

2. Wire up `ShellServices` initialization in `App.xaml.cs` startup:
   - Start ManagedShell services on application launch
   - Dispose services on application shutdown

3. Implement a basic AppBar registration helper:
   - Wrapper around ManagedShell's AppBar functionality
   - Support registering a WPF Window as a top-docked or bottom-docked AppBar
   - Handle `WM_DISPLAYCHANGE` for monitor connect/disconnect (Section 8B)

4. Log ManagedShell service status on startup for diagnostics.

## Acceptance Criteria / Tests

- [ ] `ShellManager` initializes without exceptions on application startup
- [ ] `TaskService` returns a non-empty list of running tasks when other applications are open
- [ ] `TrayService` successfully enumerates existing system tray icons
- [ ] AppBar registration helper can register and unregister a test window without errors
- [ ] Clean shutdown disposes all ManagedShell services without orphaned resources
- [ ] Unit tests mock `ShellManager` interfaces to verify initialization and disposal sequences
