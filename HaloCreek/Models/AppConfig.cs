using System;
using System.Collections.Generic;

namespace HaloCreek.Models
{
    public sealed record AppConfig(
        string CodexExecutableName,
        IReadOnlyList<string> CodexLaunchArguments,
        int MaxSessionHistoryFiles,
        string DiffToolPath,
        string DefaultWorkspacePath)
    {
        public static AppConfig DefaultForMvp1 { get; } = new(
            "codex",
            Array.Empty<string>(),
            100,
            string.Empty,
            string.Empty);
    }
}
