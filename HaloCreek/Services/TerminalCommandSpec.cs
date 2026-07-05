using System.Collections.Generic;

namespace HaloCreek.Services
{
    public abstract record TerminalCommandSpec(string WorkingDirectory);

    public sealed record TerminalExecutableCommandSpec(
        string WorkingDirectory,
        string Executable,
        IReadOnlyList<string> Arguments) : TerminalCommandSpec(WorkingDirectory);

    public sealed record TerminalWindowsCommandSpec(
        string WorkingDirectory,
        string Executable,
        IReadOnlyList<string> Arguments) : TerminalCommandSpec(WorkingDirectory);

    // todo 考虑下下面这个model要不要删 如果之后不用wsl是应该删除的
    public sealed record TerminalWslScriptCommandSpec(
        string WorkingDirectory,
        string FileNameHint,
        string ScriptText) : TerminalCommandSpec(WorkingDirectory);
}
