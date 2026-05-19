using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class ConfigService
    {
        public AppConfig LoadEffectiveConfig(string? workspacePath)
        {
            return AppConfig.DefaultForMvp1;
        }
    }
}
