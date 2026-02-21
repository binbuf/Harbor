# Harbor

Windows 11 shell replacement providing a macOS-style desktop experience.

## Build

```bash
dotnet build Harbor.slnx
```

## Test

```bash
dotnet test Harbor.slnx
```

## Run

```bash
dotnet run --project src/Harbor.Shell
```

## Project Structure

- `src/Harbor.Shell` — WPF application (main shell executable)
- `src/Harbor.Core` — Class library (shared models, interfaces, Win32 interop via CsWin32)
- `tests/Harbor.Shell.Tests` — xUnit tests for Harbor.Shell
- `tests/Harbor.Core.Tests` — xUnit tests for Harbor.Core

## Tech Stack

- .NET 10, C#, WPF
- ManagedShell (AppBar, tray, task enumeration)
- Microsoft.Windows.CsWin32 (Win32/DWM P/Invoke generation)
