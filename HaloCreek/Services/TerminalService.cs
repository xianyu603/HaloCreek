using System;
using HaloCreek.Infrastructure;

namespace HaloCreek.Services
{
    public sealed class TerminalService
    {
        private readonly PlatformInfrastructure _platformInfrastructure;
        private readonly string _frontWindowIdentity;

        public TerminalService(AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _platformInfrastructure = appCommonRuntime.PlatformInfrastructure;
            _frontWindowIdentity = "halocreek-front-" + Guid.NewGuid().ToString("N")[..8];
        }

        public void LaunchFrontClient(TerminalCommandSpec startupCommand)
        {
            ArgumentNullException.ThrowIfNull(startupCommand);

            var process = _platformInfrastructure.LaunchTerminal(new TerminalLaunchRequest(
                startupCommand,
                _frontWindowIdentity,
                TerminalWindowMode.ReuseOrCreateNamedWindow));
            if (process is null)
            {
                throw new InvalidOperationException("Failed to launch front terminal.");
            }
        }

        public void ActivateFrontClient()
        {
            var process = _platformInfrastructure.ActivateTerminalWindow(_frontWindowIdentity);
            if (process is null)
            {
                throw new InvalidOperationException("Failed to activate front terminal.");
            }
        }
    }
}
