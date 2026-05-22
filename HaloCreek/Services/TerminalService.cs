using System;
using HaloCreek.Infrastructure;

namespace HaloCreek.Services
{
    public sealed class TerminalService
    {
        public TerminalService(PlatformInfrastructure platformInfrastructure)
        {
            ArgumentNullException.ThrowIfNull(platformInfrastructure);
        }

        public void ShowFront(TerminalCommandSpec command)
        {
            ArgumentNullException.ThrowIfNull(command);
        }
    }
}
