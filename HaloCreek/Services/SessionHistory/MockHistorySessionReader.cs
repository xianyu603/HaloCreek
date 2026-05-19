using System;
using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services.SessionHistory
{
    public sealed class MockHistorySessionReader : ISessionHistoryReader
    {
        public IReadOnlyList<HistorySessionInfo> ReadSessions(string? workspacePath, AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return Array.Empty<HistorySessionInfo>();
            }

            var updatedAt = DateTimeOffset.Now;

            return new[]
            {
                new HistorySessionInfo(
                    "mvp1-layout-review",
                    "Review MVP1 layout skeleton",
                    workspacePath,
                    updatedAt.AddDays(-2),
                    updatedAt.AddHours(-3),
                    HistorySessionState.Completed,
                    "Check tab layout, footer bindings, and ViewModel boundaries."),
                new HistorySessionInfo(
                    "mvp1-service-boundaries",
                    "Draft service boundaries",
                    workspacePath,
                    updatedAt.AddDays(-1),
                    updatedAt.AddMinutes(-45),
                    HistorySessionState.Ready,
                    "Prepare workspace, session history, ongoing session, git, drag-drop, and launch services.")
            };
        }
    }
}
