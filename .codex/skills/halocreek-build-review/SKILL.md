---
name: halocreek-build-review
description: Build and validate the HaloCreek Avalonia desktop app from this repository, and only launch the Windows desktop window when the user explicitly asks for it. Use when Codex is asked to compile HaloCreek, verify build health, run the app, or start the Windows desktop window for human review.
---

# HaloCreek Build Review

## Context

HaloCreek is an Avalonia desktop app at `HaloCreek/HaloCreek.csproj`.
The project targets `net10.0-windows10.0.18362.0`.

Prefer Windows PowerShell execution from the repository root. The default build command is:

```powershell
dotnet build .\HaloCreek\HaloCreek.csproj
```

Use Windows PowerShell even when the agent is hosted through `psmux`; do not switch to WSL/bash commands unless the user explicitly asks for WSL-specific diagnosis.

## Build Validation

Run build from the repository root:

```powershell
dotnet build .\HaloCreek\HaloCreek.csproj
```

Treat the build as passing only when the output reports zero warnings and zero errors, for example `0 Warning(s)` and `0 Error(s)` or the localized equivalent.
Report the output assembly path if present, normally:

```text
D:\work\HaloCreek\HaloCreek\bin\Debug\net10.0-windows10.0.18362.0\HaloCreek.dll
```

If the build fails because `HaloCreek.exe`, `HaloCreek.dll`, or another output file is locked by a running HaloCreek process, report the locked file/process from the build output. Do not kill the process, do not switch to an alternate output directory, and do not launch another app window unless the user explicitly asks for that recovery step.

## Launch For Review

Only launch the app when the user explicitly asks to open or review the desktop window. Do this after all requested edits, builds, and checks are complete. Do not launch automatically after a successful build.

When launch is explicitly requested, launch the already-built Windows executable detached from the agent process by using the clean launch script:

```powershell
.\.codex\skills\halocreek-build-review\Start-HaloCreekClean.ps1
```

The script removes `psmux`/terminal-multiplexer environment variables from its own process before starting HaloCreek, so the launched desktop app does not inherit the agent pane environment. Use this script instead of calling `Start-Process` directly.

Then report that the app was launched and leave the window open for the user. Do not stop the process unless the user explicitly asks to close it.

If a foreground health check is needed, run this only for diagnosis and say that it will block until the app exits:

```powershell
dotnet run --project .\HaloCreek\HaloCreek.csproj
```
