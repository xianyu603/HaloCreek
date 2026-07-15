using System;
using HaloCreek.Infrastructure;

namespace HaloCreek.Services
{
    public sealed class TerminalService
    {
        private readonly PlatformInfrastructure _platformInfrastructure;

        public TerminalService(AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _platformInfrastructure = appCommonRuntime.PlatformInfrastructure;
        }

        public void LaunchFrontClient(TerminalCommandSpec startupCommand)
        {
            ArgumentNullException.ThrowIfNull(startupCommand);

            var process = _platformInfrastructure.LaunchTerminal(startupCommand);
            if (process is null)
            {
                throw new InvalidOperationException("Failed to launch front terminal.");
            }
        }
    }
}
