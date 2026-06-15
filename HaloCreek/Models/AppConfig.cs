using System;
using System.Collections.Generic;

namespace HaloCreek.Models
{
    public sealed record AppConfig(
        string CodexExecutableName,
        IReadOnlyList<string> CodexLaunchArguments,
        int MaxSessionHistoryFiles)
    {
        public static AppConfig DefaultForMvp1 { get; } = new(
            "codex",
            Array.Empty<string>(),
            100);
    }
}
