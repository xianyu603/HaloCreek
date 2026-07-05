using HaloCreek.Services;

namespace HaloCreek.Infrastructure
{
    public sealed record TerminalLaunchRequest(
        TerminalCommandSpec Command,
        string? WindowIdentity);
}
