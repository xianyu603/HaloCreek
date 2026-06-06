using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed record WorkspaceContext(
        string WorkspacePath,
        AppConfig EffectiveConfig,
        string GitRootPath);
}
