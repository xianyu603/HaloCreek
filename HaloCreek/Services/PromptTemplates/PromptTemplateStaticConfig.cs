using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services.PromptTemplates
{
    internal static class PromptTemplateStaticConfig
    {
        public static IReadOnlyList<PromptTemplateItem> Items { get; } =
        [
            new(
                "实现功能",
                "描述目标、范围、验收和验证方式。",
                """
                实现下面的功能。

                目标：
                -

                范围：
                -

                验收标准：
                -

                完成后运行相关 build/test；如果没法跑，说明原因。
                """),
            new(
                "修 Bug",
                "说明现象、期望、复现和验证方式。",
                """
                修复下面的问题。

                现象：
                -

                期望：
                -

                复现步骤：
                -

                完成后运行相关 build/test；如果没法跑，说明原因。
                """),
        ];
    }
}
