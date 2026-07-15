using System.Collections.Generic;

namespace HaloCreek.Services
{
    public abstract record TerminalCommandSpec(
        string WorkingDirectory,
        string WindowTitle);

    public sealed record TerminalWindowsCommandSpec(
        string WorkingDirectory,
        string WindowTitle,
        string Executable,
        IReadOnlyList<string> Arguments) : TerminalCommandSpec(WorkingDirectory, WindowTitle);
}
