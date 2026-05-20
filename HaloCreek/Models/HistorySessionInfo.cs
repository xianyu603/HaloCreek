using System;
using System.Linq;

namespace HaloCreek.Models
{
    public sealed record HistorySessionInfo(
        string Id,
        string WorkspacePath,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastUpdatedAt,
        string InitialPrompt,
        string LastPrompt,
        string LastReply,
        string SessionFilePath)
    {
        public string InitialPromptText => RemoveBlankLines(InitialPrompt);
        // 这里的Getter是model还是 model view? 不纠结再用到的时候再说
        public string SessionSummaryText
        {
            get
            {
                var initialPrompt = RemoveBlankLines(InitialPrompt);
                var lastPrompt = RemoveBlankLines(LastPrompt);
                string ret = $"Initial Prompt:{Environment.NewLine}{initialPrompt}{Environment.NewLine}{Environment.NewLine}";

                if (!string.Equals(lastPrompt.Trim(), initialPrompt.Trim(), StringComparison.Ordinal))
                {
                    ret += $"Last Prompt:{Environment.NewLine}{lastPrompt}{Environment.NewLine}{Environment.NewLine}";
                }
                ret += $"Last Reply:{Environment.NewLine}{RemoveBlankLines(LastReply)}";
                return ret;
            }
        }

        public string LatestActivityText
        {
            get
            {
                var lastPrompt = RemoveBlankLines(LastPrompt).Trim();
                var lastReply = RemoveBlankLines(LastReply).Trim();
                var initialPrompt = InitialPromptText.Trim();

                if (lastPrompt.Length == 0 ||
                    string.Equals(lastPrompt, initialPrompt, StringComparison.Ordinal))
                {
                    return lastReply;
                }

                if (lastReply.Length == 0)
                {
                    return $"Last prompt: {lastPrompt}";
                }

                return $"Last prompt: {lastPrompt}{Environment.NewLine}Last reply: {lastReply}";
            }
        }

        private static string RemoveBlankLines(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            return string.Join(
                Environment.NewLine,
                lines.Where(line => !string.IsNullOrWhiteSpace(line)));
        }
    }
}
