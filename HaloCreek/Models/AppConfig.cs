using System;
using System.Collections.Generic;

namespace HaloCreek.Models
{
    public sealed record AppConfig(
        string CodexExecutableName,
        IReadOnlyList<string> CodexLaunchArguments,
        string SessionHistoryRootPath,
        string DiffToolPath)
    {
        public static AppConfig DefaultForMvp1 { get; } = new(
            "codex",
            Array.Empty<string>(),
            string.Empty,
            string.Empty);
    }
}
