using System;
using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services.SessionHistory
{
    public sealed class MockHistorySessionReader : ISessionHistoryReader
    {
        public SessionHistoryResult ReadSessions(string? workspacePath, AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return new SessionHistoryResult(Array.Empty<HistorySessionInfo>(), 0);
            }

            return new SessionHistoryResult(Array.Empty<HistorySessionInfo>(), 0);

            //return new[]
            //{
            //new HistorySessionInfo(
            //    "mvp1-layout-review",
            //    workspacePath,
            //    updatedAt.AddDays(-2),
            //    updatedAt.AddHours(-3),
            //    "Check tab layout, footer bindings, and ViewModel boundaries.",
            //    "Tighten the row layout and make the empty state less noisy.",
            //    "Updated the list spacing and preserved room for actions.",
            //    "/mock/codex/sessions/2026/05/18/mvp1-layout-review.jsonl"),
            //new HistorySessionInfo(
            //    "mvp1-service-boundaries",
            //    workspacePath,
            //    updatedAt.AddDays(-1),
            //    updatedAt.AddMinutes(-45),
            //    "Prepare workspace, session history, ongoing session, git, drag-drop, and launch services.",
            //    "Keep JSONL parsing isolated from the UI.",
            //    "Reader produces stable HistorySessionInfo records consumed by ViewModels.",
            //    "/mock/codex/sessions/2026/05/19/mvp1-service-boundaries.jsonl"),
            //new HistorySessionInfo(
            //    "mvp1-wsl-paths",
            //    workspacePath,
            //    updatedAt.AddHours(-7),
            //    updatedAt.AddMinutes(-12),
            //    "Compare Windows and WSL workspace paths before wiring Codex history files.",
            //    "Compare D:\\work\\HaloCreek with /mnt/d/work/HaloCreek.",
            //    "Keep path equivalence below the ViewModel so UI stays platform neutral.",
            //    "/mock/codex/sessions/2026/05/20/mvp1-wsl-paths.jsonl")
            //};
        }
    }
}
