using System;

namespace HaloCreek.Infrastructure
{
    public static class FirstPromptSummaryFormatter
    {
        private const int FirstPromptSummaryMaxLength = 20;

        public static string Format(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var summary = string.Join(
                " ",
                text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            if (summary.Length > FirstPromptSummaryMaxLength)
            {
                summary = summary[..FirstPromptSummaryMaxLength];
            }

            return summary;
        }
    }
}
