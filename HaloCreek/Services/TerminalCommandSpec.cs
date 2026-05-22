using System.Collections.Generic;

namespace HaloCreek.Services
{
    public sealed record TerminalCommandSpec(
        string WorkingDirectory,
        string Executable,
        IReadOnlyList<string> Arguments);
}
