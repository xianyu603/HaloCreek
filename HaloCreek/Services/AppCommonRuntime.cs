using System;
using HaloCreek.Infrastructure;

namespace HaloCreek.Services
{
    public sealed class AppCommonRuntime
    {
        public AppCommonRuntime(
            PlatformInfrastructure platformInfrastructure,
            ApplicationStatusService applicationStatusService,
            TransientEventService transientEventService)
        {
            PlatformInfrastructure = platformInfrastructure
                ?? throw new ArgumentNullException(nameof(platformInfrastructure));
            ApplicationStatusService = applicationStatusService
                ?? throw new ArgumentNullException(nameof(applicationStatusService));
            TransientEventService = transientEventService
                ?? throw new ArgumentNullException(nameof(transientEventService));
        }

        public PlatformInfrastructure PlatformInfrastructure { get; }

        public ApplicationStatusService ApplicationStatusService { get; }

        public TransientEventService TransientEventService { get; }
    }
}
