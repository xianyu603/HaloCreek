namespace HaloCreek.Models
{
    public sealed record ReviewFilePath(string RelativePath)
    {
        public string DisplayPath => RelativePath;
    }
}
