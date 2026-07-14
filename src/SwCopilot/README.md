# Legacy SwCopilot research code

此目录是 2026-07-13 之前的 SOLIDWORKS Add-in 可行性预研快照，不是当前 Text2CAD 实现。

请注意：

- 不要把本目录加入新的 Text2CAD Solution 或依赖图。
- 不要按照其中的旧 `.csproj`、类型名称或三条硬编码命令继续开发。
- 源码注释里的 `spec section ...` 指向已经退役的历史规格，和当前 [`text2cad_mvp_spec.md`](../../text2cad_mvp_spec.md) 章节不对应。
- 此代码使用旧的单进程、.NET Framework 4.8、SOLIDWORKS 2025 PIA 假设，没有实现当前要求的 Host 隔离、版本化 IR、revision、plan hash、最终校验和可验证恢复。
- 旧实现没有完成新的 SOLIDWORKS 2025 基线与 2026 兼容矩阵真机验收，不得用于发布或生产模型。

保留该目录仅用于回顾早期 API 试验和已发现的风险，例如单位换算、本地化基准面、选择状态与 rebuild 行为。任何经验都必须在新的 Windows 11 + SOLIDWORKS 2025 基线和 2026 兼容环境中重新验证。

当前项目入口见仓库根目录的 [`README.md`](../../README.md)。
