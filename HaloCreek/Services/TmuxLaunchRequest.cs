using System.Collections.Generic;

namespace HaloCreek.Services
{
    public sealed record TmuxLaunchRequest(
        string WorkspacePath,
        string ExecutableName,
        IReadOnlyList<string> Arguments,
        string Title);
}
