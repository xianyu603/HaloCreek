namespace HaloCreek.Models
{
    public abstract record SessionRestartSource
    {
        private SessionRestartSource()
        {
        }

        public sealed record LaunchPrompt(string PromptText) : SessionRestartSource;

        public sealed record CodexSession(string SessionId) : SessionRestartSource;
    }
}
