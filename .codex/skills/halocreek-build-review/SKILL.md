---
name: halocreek-build-review
description: Build, validate, and launch the HaloCreek Avalonia desktop app from this repository. Use when Codex is asked to compile HaloCreek, verify build health, run the app, or start the Windows desktop window for human review from WSL/agent context.
---

# HaloCreek Build Review

## Context

HaloCreek is an Avalonia desktop app at `HaloCreek/HaloCreek.csproj`.
The project targets `net10.0` and should be built with the Windows-side .NET SDK from WSL:

```bash
'/mnt/c/Program Files/dotnet/dotnet.exe' build HaloCreek/HaloCreek.csproj
```

Prefer Windows-side execution because the app is intended to open a Windows desktop window.

## Build Validation

Run build from the repository root:

```bash
'/mnt/c/Program Files/dotnet/dotnet.exe' build HaloCreek/HaloCreek.csproj
```

Treat the build as passing only when the output says `0 个警告` and `0 个错误` or the equivalent zero-warning/zero-error success message.
Report the output assembly path if present, normally:

```text
D:\work\halocreek\HaloCreek\bin\Debug\net10.0\HaloCreek.dll
```

If Windows `.exe` interop fails with `cannot execute binary file: Exec format error`, check `/etc/wsl.conf` for:

```ini
[interop]
enabled=true
appendWindowsPath=true
```

After changing that file, the user must run `wsl --shutdown` from Windows PowerShell and reopen WSL before retrying.

## Launch For Review

When the user wants to review the window, launch the already-built Windows executable detached from the agent process. Run this from the repository root and keep the app path relative to the workspace:

```bash
/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe -NoProfile -Command "Start-Process -FilePath '.\HaloCreek\bin\Debug\net10.0\HaloCreek.exe'"
```

Then report that the app was launched and leave the window open for the user. Do not stop the process unless the user explicitly asks to close it.

If a foreground health check is needed, run this only for diagnosis and say that it will block until the app exits:

```bash
'/mnt/c/Program Files/dotnet/dotnet.exe' run --project HaloCreek/HaloCreek.csproj
```
