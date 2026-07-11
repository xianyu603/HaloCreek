using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services.PromptTemplates
{
    internal static class PromptTemplateStaticConfig
    {
        public static IReadOnlyList<PromptTemplateItem> Items { get; } =
        [
            new(
                "Implement feature",
                "Describe the goal, scope, acceptance criteria, and validation.",
                """
                Implement the feature below.

                Goal:
                -

                Scope:
                -

                Acceptance criteria:
                -

                When done, run the relevant build/test; if you cannot run them, explain why.
                """),
            new(
                "Fix bug",
                "Describe the symptom, expected behavior, reproduction steps, and validation.",
                """
                Fix the issue below.

                Symptom:
                -

                Expected behavior:
                -

                Reproduction steps:
                -

                When done, run the relevant build/test; if you cannot run them, explain why.
                """),
        ];
    }
}
