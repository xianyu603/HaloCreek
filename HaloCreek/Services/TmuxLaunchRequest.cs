using System.Collections.Generic;

namespace HaloCreek.Services
{
    public sealed record TmuxLaunchRequest(
        string WorkspacePath,
        string ExecutableName,
        IReadOnlyList<string> Arguments,
        string Title,
        string? HistoryPromptText,
        string? KnownCodexSessionId);

    public sealed record TmuxLaunchResult(
        string TmuxSessionId,
        string CodexSessionId);
}
