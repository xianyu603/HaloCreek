using System.Collections.Generic;

namespace HaloCreek.Services
{
    public abstract record TerminalCommandSpec(string WorkingDirectory);

    public sealed record TerminalExecutableCommandSpec(
        string WorkingDirectory,
        string Executable,
        IReadOnlyList<string> Arguments) : TerminalCommandSpec(WorkingDirectory);

    public sealed record TerminalWslScriptCommandSpec(
        string WorkingDirectory,
        string FileNameHint,
        string ScriptText) : TerminalCommandSpec(WorkingDirectory);
}
