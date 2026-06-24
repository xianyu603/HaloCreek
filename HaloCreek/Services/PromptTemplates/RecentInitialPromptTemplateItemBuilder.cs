using System;
using System.Collections.Generic;
using HaloCreek.Infrastructure;
using HaloCreek.Models;

namespace HaloCreek.Services.PromptTemplates
{
    internal static class RecentInitialPromptTemplateItemBuilder
    {
        private const int MaxRecentInitialPrompts = 5;

        public static IReadOnlyList<PromptTemplateItem> Build(
            IReadOnlyList<HistorySessionInfo> sessions)
        {
            ArgumentNullException.ThrowIfNull(sessions);

            var items = new List<PromptTemplateItem>(MaxRecentInitialPrompts);
            var seenPrompts = new HashSet<string>(StringComparer.Ordinal);

            foreach (var session in sessions)
            {
                if (string.IsNullOrWhiteSpace(session.InitialPrompt))
                {
                    continue;
                }

                var dedupeKey = session.InitialPrompt.Trim();
                if (!seenPrompts.Add(dedupeKey))
                {
                    continue;
                }

                var title = FirstPromptSummaryFormatter.Format(session.InitialPrompt);
                if (title.Length == 0)
                {
                    continue;
                }

                items.Add(new PromptTemplateItem(
                    title,
                    $"Updated {session.LastUpdatedAtLocalText}",
                    session.InitialPrompt));

                if (items.Count == MaxRecentInitialPrompts)
                {
                    break;
                }
            }

            return items;
        }
    }
}
