# Coding Rules

## 文本格式

- 仓库内受版本控制的文本文件统一使用 CRLF 行尾。
- 仓库内受版本控制的文本文件统一使用 UTF-8 with BOM 编码。
- Codex skill 文件 `.codex/skills/**/SKILL.md` 例外：必须使用 UTF-8 without BOM 编码，否则 Codex skill loader 无法识别 YAML frontmatter。
- Git 属性文件 `.gitattributes` 例外：必须使用 UTF-8 without BOM 编码，避免 Git 将 BOM 识别为属性规则内容。
- 新增或修改源码、XAML、项目文件、配置文件、脚本和文档时，除上述例外外，必须保持上述行尾和编码。
- 二进制文件、构建输出、IDE 缓存和其他生成产物不适用本规则。

## 修改要求

修改代码前先阅读本文档，并在提交前确认改动文件仍符合文本格式规则。
