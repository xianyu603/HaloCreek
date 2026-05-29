using System;
using HaloCreek.Infrastructure;

namespace HaloCreek.Services
{
    public sealed class TerminalService
    {
        private readonly PlatformInfrastructure _platformInfrastructure;
        private readonly string _frontWindowIdentity;
        private bool _frontClientStarted;

        public TerminalService(AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _platformInfrastructure = appCommonRuntime.PlatformInfrastructure;
            _frontWindowIdentity = "halocreek-front-" + Guid.NewGuid().ToString("N")[..8];
        }

        public void EnsureFrontClient(TerminalCommandSpec startupCommand)
        {
            ArgumentNullException.ThrowIfNull(startupCommand);

            if (!_frontClientStarted)
            {
                var process = _platformInfrastructure.LaunchTerminal(new TerminalLaunchRequest(
                    startupCommand,
                    _frontWindowIdentity,
                    TerminalWindowMode.ReuseOrCreateNamedWindow));
                _frontClientStarted = process is not null;
                return;
            }

            _platformInfrastructure.ActivateTerminalWindow(_frontWindowIdentity);
        }
    }
}
