using System.Collections.Generic;

namespace HaloCreek.Services.Completions.ShortcutPhrases
{
    public static class ShortcutPhraseStaticConfig
    {
        public static IReadOnlyList<ShortcutPhraseCategory> Categories { get; } =
        [
            new(
                "Mode",
                ["mode", "m", "first"],
                "Decide whether this turn should change files and what kind of judgment comes first.",
                [
                    new(
                        "Report first",
                        ["report", "plan"],
                        "Give the conclusion or plan first without editing files.",
                        "Give the conclusion or plan first without editing files."),
                    new(
                        "Implement directly",
                        ["implement", "do", "code"],
                        "Complete the change directly from the current context.",
                        "Implement this request directly, keeping the change as small as practical."),
                    new(
                        "Review only",
                        ["review", "inspect", "check"],
                        "List issues in a review tone without editing files.",
                        "Use a code review tone to list issues and risks first. Do not make changes."),
                    new(
                        "Write plan",
                        ["plan", "design"],
                        "Write a plan to disk.",
                        "Use the existing context and write a plan under docs/."),
                    new(
                        "Review plan",
                        ["plan review", "proposal review"],
                        "Review the workspace plan.",
                        "Review the workspace plan, focusing on obvious contradictions or risks."),
                ]),
            new(
                "Scope",
                ["scope", "s", "limit"],
                "Limit the files, behavior, and dependency boundaries that may change.",
                [
                    new(
                        "No refactor",
                        ["refactor", "refactoring"],
                        "Keep the existing structure unless a change is necessary.",
                        "Do not perform unrelated refactoring; adjust structure only when it is necessary for this request."),
                    new(
                        "Keep public contracts",
                        ["contract", "protocol", "format", "api", "schema"],
                        "Preserve public behavior, configuration, and data formats.",
                        "Do not change public behavior, configuration formats, or data formats unless the request explicitly requires it."),
                    new(
                        "Preserve existing changes",
                        ["user changes", "dirty", "git"],
                        "Do not roll back user changes or changes from other sessions.",
                        "Preserve existing user changes and do not roll back anything I did not ask you to roll back."),
                ]),
            new(
                "Finish",
                ["finish", "done", "check", "verify", "f"],
                "Define verification before completion and the final delivery behavior.",
                [
                    new(
                        "Run relevant checks",
                        ["verify", "build", "test"],
                        "Run checks related to the change after finishing.",
                        "Run the relevant build after finishing."),
                    new(
                        "Skip build",
                        ["no build", "skip build"],
                        "Skip the build explicitly, but explain suggested validation.",
                        "Do not build; after the change, explain the impact scope and suggested validation."),
                    new(
                        "Do not commit",
                        ["commit", "no commit", "working tree"],
                        "Leave the changes in the working tree for human review.",
                        "Leave the changes in the working tree for my review. Do not commit."),
                    new(
                        "Commit when done",
                        ["commit", "git"],
                        "Create one commit after validation.",
                        "After completing and validating the change, create one commit with a concise message."),
                    new(
                        "Build and launch app",
                        ["run", "app", "launch"],
                        "Build after finishing and open the app window for manual inspection.",
                        "After finishing, build and launch the app so I can inspect it manually."),
                ]),
        ];
    }
}
