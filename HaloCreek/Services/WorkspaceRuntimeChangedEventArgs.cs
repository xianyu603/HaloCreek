using System;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class WorkspaceRuntimeChangedEventArgs : EventArgs
    {
        public WorkspaceRuntimeChangedEventArgs(string workspacePath, AppConfig effectiveConfig)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
            EffectiveConfig = effectiveConfig ?? throw new ArgumentNullException(nameof(effectiveConfig));

            WorkspacePath = workspacePath;
        }

        public string WorkspacePath { get; }

        public AppConfig EffectiveConfig { get; }
    }
}
