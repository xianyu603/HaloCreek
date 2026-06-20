using System.Collections.Generic;

namespace HaloCreek.Services.Completions
{
    internal static class ShortcutPhraseStaticConfig
    {
        public static IReadOnlyList<ShortcutPhraseCategory> Categories { get; } =
        [
            new ShortcutPhraseCategory(
                "改",
                ["change", "modify", "edit"],
                "修改、实现或调整现有内容",
                [
                    new ShortcutPhraseItem(
                        "实现方案",
                        ["implement", "impl", "落实"],
                        "按已给方案完成代码改动并验证",
                        "按方案实现，保持改动范围最小，完成后运行可行的验证"),
                    new ShortcutPhraseItem(
                        "修改代码",
                        ["code", "fix"],
                        "直接修改代码并说明验证结果",
                        "直接修改代码，遵循现有项目风格，完成后说明改动和验证结果"),
                    new ShortcutPhraseItem(
                        "只改代码",
                        ["code-only"],
                        "不编译，只完成代码层面改动",
                        "只改代码，暂时不用编译，完成后说明未验证项"),
                ]),
            new ShortcutPhraseCategory(
                "查",
                ["find", "inspect", "look"],
                "查看、定位或解释项目现状",
                [
                    new ShortcutPhraseItem(
                        "解释代码",
                        ["explain"],
                        "说明相关代码路径和当前行为",
                        "阅读相关代码，解释当前实现路径、关键逻辑和可能的风险点"),
                    new ShortcutPhraseItem(
                        "定位入口",
                        ["entry"],
                        "找出功能入口和主要调用链",
                        "定位这个功能的入口、主要调用链和关键文件，先不要改代码"),
                    new ShortcutPhraseItem(
                        "查看差异",
                        ["diff"],
                        "总结当前工作区改动",
                        "查看当前工作区差异，总结已修改文件、行为变化和需要注意的点"),
                ]),
            new ShortcutPhraseCategory(
                "审",
                ["review", "check"],
                "审查代码、方案或变更风险",
                [
                    new ShortcutPhraseItem(
                        "审查代码",
                        ["code-review", "cr"],
                        "按 code review 方式列出问题",
                        "按 code review 方式审查这次改动，优先列出 bug、回归风险和缺失验证"),
                    new ShortcutPhraseItem(
                        "审查方案",
                        ["plan-review"],
                        "检查方案是否完整可执行",
                        "审查这个方案是否完整、边界是否清晰、是否存在实现风险，先不要改代码"),
                    new ShortcutPhraseItem(
                        "补测试建议",
                        ["tests", "test"],
                        "指出应该补哪些验证",
                        "根据当前改动建议需要补充的测试或手动验证路径，并说明优先级"),
                ]),
        ];
    }
}
