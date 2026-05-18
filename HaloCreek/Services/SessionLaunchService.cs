using System;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class SessionLaunchService
    {
        public SessionLaunchResult Launch(
            WorkspaceInfo workspace,
            string promptText,
            AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentException.ThrowIfNullOrWhiteSpace(promptText);
            ArgumentNullException.ThrowIfNull(config);

            return new SessionLaunchResult(
                false,
                "Session launch is not connected yet.",
                null);
        }
    }

    public sealed record SessionLaunchResult(
        bool Started,
        string StatusMessage,
        int? ProcessId);
}
