using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class ConfigService
    {
        public AppConfig LoadEffectiveConfig(WorkspaceInfo? workspace)
        {
            return AppConfig.DefaultForMvp1;
        }
    }
}
