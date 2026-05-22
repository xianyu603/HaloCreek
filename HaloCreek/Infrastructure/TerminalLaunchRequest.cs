using HaloCreek.Services;

namespace HaloCreek.Infrastructure
{
    public sealed record TerminalLaunchRequest(
        TerminalCommandSpec Command,
        string? WindowIdentity,
        TerminalWindowMode WindowMode);

    public enum TerminalWindowMode
    {
        Default,
        ReuseOrCreateNamedWindow
    }
}
