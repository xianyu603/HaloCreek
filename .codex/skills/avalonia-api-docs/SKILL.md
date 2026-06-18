---
name: avalonia-api-docs
description: Find version-matched Avalonia API documentation for HaloCreek and other Avalonia projects. Use when Codex needs to verify Avalonia controls, properties, methods, XAML APIs, package behavior, or migration details before writing or reviewing Avalonia code.
---

# Avalonia API Docs

## Workflow

1. Prefer project-local package documentation over web search.
   - Read the project's `obj/project.assets.json` to get the exact Avalonia package versions and NuGet package folder.
   - For HaloCreek, the current restored packages are under the Windows NuGet cache path recorded in `project.assets.json`, for example `C:\Users\cuika\.nuget\packages\`, which is `/mnt/c/Users/cuika/.nuget/packages/` from WSL.
   - Prefer `ref/<target-framework>/*.xml` for API surface documentation; fall back to `lib/<target-framework>/*.xml`.

2. Use the bundled search script for API lookup:

```bash
python3 .codex/skills/avalonia-api-docs/scripts/search_avalonia_docs.py WindowStartupLocation
python3 .codex/skills/avalonia-api-docs/scripts/search_avalonia_docs.py ShowDialog Window
python3 .codex/skills/avalonia-api-docs/scripts/search_avalonia_docs.py --list-files
```

3. If the local XML docs are missing, run restore/build first if appropriate for the task. In HaloCreek, prefer the repository build skill for build validation.

4. Use online docs only as secondary context:
   - API reference: `https://api-docs.avaloniaui.net/`
   - Concept docs: `https://docs.avaloniaui.net/`
   - Check the displayed version. Do not assume online API pages match the project's restored package version.

5. When an API remains ambiguous, inspect the referenced assemblies or source package rather than guessing. If the API field, enum value, overload, or XAML syntax cannot be verified, report that explicitly.

## Practical Notes

- HaloCreek targets `net10.0` and currently references Avalonia `12.0.3`; the local package XML is the most reliable source for that exact API surface.
- Common XML files are `Avalonia.Controls.xml`, `Avalonia.Base.xml`, `Avalonia.Markup.Xaml.xml`, `Avalonia.Desktop.xml`, and `Avalonia.Themes.Fluent.xml`.
- The script searches XML member names and documentation text, then prints member IDs and compact summaries. Use the member ID to locate exact overloads.
