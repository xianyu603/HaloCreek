using System.Collections.Generic;

namespace HaloCreek.Services.Completions.ShortcutPhrases
{
    internal static class ShortcutPhraseStaticConfig
    {
        public static IReadOnlyList<ShortcutPhraseCategory> Categories { get; } =
        [
            new(
                "模式",
                ["mode", "m", "先"],
                "决定这轮是否直接动手，以及先做哪类判断。",
                [
                    new(
                        "先口头汇报",
                        ["口头", "汇报", "plan"],
                        "先给结论或方案，不改文件。",
                        "先给结论或方案，不改文件。"),
                    new(
                        "直接实现",
                        ["实现", "do", "code"],
                        "按现有上下文直接完成修改。",
                        "直接实现这个需求，保持修改范围尽量小。"),
                    new(
                        "只做审查",
                        ["review", "审查", "检查"],
                        "按 review 口吻列问题，不改文件。",
                        "按 code review 口吻先列问题和风险，不做修改。"),
                    new(
                        "撰写方案",
                        [],
                        "写方案并落盘",
                        "参考之前的信息，落盘一个方案到docs/"),
                    new(
                        "方案审阅",
                        [],
                        "审阅工作区方案",
                        "审阅工作区方案，重点看有没有明显的矛盾或风险。"),
                ]),
            new(
                "范围",
                ["scope", "s", "不"],
                "限制可以修改的文件、行为和依赖边界。",
                [
                    new(
                        "不重构",
                        ["重构", "refactor"],
                        "保留既有结构，除非必要。",
                        "不要做无关重构；只有为完成本需求确实必要时才调整结构。"),
                    new(
                        "不改协议",
                        ["协议", "格式", "api", "schema"],
                        "保护公开行为、配置和数据格式。",
                        "不要改公开行为、配置格式或数据格式，除非需求明确要求。"),
                    new(
                        "保留已有改动",
                        ["用户改动", "dirty", "git"],
                        "不要回滚用户或其他会话的修改。",
                        "保留用户已有改动，不要回滚我没要求回滚的内容。"),
                ]),
            new(
                "完成",
                ["finish", "done", "check", "verify", "f"],
                "定义完成前的验证动作和最终交付方式。",
                [
                    new(
                        "跑相关检查",
                        ["测试", "编译", "build", "test"],
                        "完成后运行和改动相关的检查。",
                        "完成后运行相关 build/test；如果没法跑，说明原因。"),
                    new(
                        "不用编译",
                        ["不要编译", "no build"],
                        "明确跳过 build，但说明验证建议。",
                        "不用编译；改完说明影响范围和建议验证方式。"),
                    new(
                        "不提交",
                        ["commit", "no commit", "working tree"],
                        "停在工作区，等待人工 review。",
                        "停在 working tree，让我 review，不要 commit。"),
                    new(
                        "完成并提交",
                        ["commit", "提交", "git"],
                        "验证后创建一个 commit。",
                        "完成并验证后创建一个 commit，message 简洁说明变化。"),
                    new(
                        "完成后编译并启动应用",
                        ["启动", "运行", "app", "launch"],
                        "完成后 build，并打开应用窗口供人工检查。",
                        "完成后编译并启动应用，让我人工检查。"),
                ]),
        ];
    }
}
