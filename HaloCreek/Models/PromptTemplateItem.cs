namespace HaloCreek.Models
{
    // TODO: 该模型已同时用于内置模板和最近 initial prompt，后续重命名为更通用的插入候选模型。
    public sealed record PromptTemplateItem(
        string Title,
        string Description,
        string InsertText);
}
