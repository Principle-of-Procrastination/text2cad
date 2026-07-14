# Legacy deployment experiments

此目录中的 `install.bat`、`uninstall.bat` 和 `SwCopilot.iss` 只服务于旧 `src/SwCopilot` 预研 DLL。

**不要把这些脚本用于新的 Text2CAD 实现，也不要在当前开发机上运行它们。**

它们缺少新产品需要的双进程安装、Host 生命周期、协议版本、签名、更新、回滚和 SOLIDWORKS 2025 基线/2026 兼容验证。新安装方案将在 M4 产品化阶段重新设计，至少需要同时部署：

- `Text2Cad.Addin`（.NET Framework 4.8、x64）；
- `Text2Cad.Host`（.NET 10 LTS、x64）；
- 版本化协议与 schema；
- 日志/诊断目录和安全的本机 IPC 配置；
- 签名、升级与卸载策略。

当前基线见 [`text2cad_mvp_spec.md`](../text2cad_mvp_spec.md)。
