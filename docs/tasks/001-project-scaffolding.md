# Task 001: Project Scaffolding

**Priority:** 1 (Foundation)
**Depends on:** None
**Blocks:** All subsequent tasks

## Objective

Create the .NET solution structure, WPF application project, and foundational configuration required by all other Harbor components.

## Technical Reference

Refer to `docs/Design.md` Sections 2 (Technical Stack) and 8A (Per-Monitor DPI Awareness) for technology selections and manifest requirements.

## Requirements

1. Create a .NET 10 solution (`Harbor.sln`) with the following projects:
   - `Harbor.Shell` — WPF Application (main shell executable)
   - `Harbor.Core` — Class Library (shared models, interfaces, Win32 interop)
   - `Harbor.Shell.Tests` — xUnit test project for Harbor.Shell
   - `Harbor.Core.Tests` — xUnit test project for Harbor.Core

2. Configure the `Harbor.Shell` application manifest (`app.manifest`):
   - Set `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2` (per Design.md Section 8A)
   - Set `requestedExecutionLevel` to `asInvoker` (no elevation required — Section 11A)

3. Add NuGet package references:
   - `ManagedShell` (pin to a specific release tag — Section 2.1)
   - `Microsoft.Windows.CsWin32` or manually define P/Invoke signatures (for Win32/DWM APIs listed in Appendix A)

4. Create a minimal `App.xaml` / `App.xaml.cs` entry point that launches an empty WPF window (placeholder for the shell).

5. Set up a `CLAUDE.md` at the repo root with build/test commands for the project.

## Acceptance Criteria / Tests

- [ ] Solution builds successfully with `dotnet build`
- [ ] Test projects run with `dotnet test` and produce a passing (empty) result
- [ ] Application manifest correctly declares per-monitor DPI awareness (parse manifest XML to verify)
- [ ] ManagedShell NuGet package is resolvable and the project compiles against it
- [ ] Application launches and displays an empty window, then exits cleanly
