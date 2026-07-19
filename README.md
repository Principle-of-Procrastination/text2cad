# Text2CAD

Text2CAD 是一个面向 SOLIDWORKS 的 AI 辅助建模项目。它不以 mesh、STEP 或 Imported Body 作为保留设计意图的主表示，而是把自然语言和当前模型上下文转换为**受控、可预览、可验证的建模计划**，再由本地确定性执行器创建或修改 SOLIDWORKS 原生特征树。

> 项目于 2026-07-13 重新从零启动。现有 `src/SwCopilot/` 与 `deploy/` 只保留为早期可行性研究，不是当前实现，不应继续作为产品代码构建或发布。

## 产品闭环

```text
用户意图 + 当前文档/选择上下文
        ↓
Text2CAD Host 生成版本化 Execution Plan
        ↓
Add-in 本地校验 capability、参数、文档 revision 与对象引用
        ↓
Task Pane 展示影响范围和风险，用户确认不可变 plan hash
        ↓
确定性 Executor 调用 SOLIDWORKS API
        ↓
Rebuild + 后置条件验证 + 成功提交 / 失败恢复
        ↓
原生、可继续编辑的 FeatureManager Tree
```

模型不会获得任意 C#、VBA、宏或 SOLIDWORKS API 执行权限。LLM 只能产生白名单内的语义 IR；最终执行权始终位于本地 Add-in。

## 2026 技术基线

| 层 | 基线 | 职责 |
|---|---|---|
| 开发环境 | Windows 11 x64 + Visual Studio Community 2026 | 构建、调试和真机验证 |
| CAD 宿主 | SOLIDWORKS 2026 x64 + 同版本 SOLIDWORKS API SDK/PIA | 文档、选择、原生特征树和 COM API |
| 进程内 Add-in | C# + .NET Framework 4.8 + x64 | `ISwAddin`、Task Pane、上下文快照、COM 调度、安全执行 |
| 进程外 Host | C# + .NET 10 LTS + x64 | LLM、Planner、会话、策略、日志和协议编排 |
| 进程通信 | 本机 Named Pipe + 版本化 DTO | 只传数据，不传 COM 对象、RCW、指针或可执行代码 |
| UI | WinForms/WPF 外壳；需要时嵌入 WebView2 | 计划预览、确认、进度、错误与恢复 |

MVP 只支持 SOLIDWORKS 2026 x64。其官方 C# Add-in 路径仍以 .NET Framework PIA、COM、`ISwAddin` 和 `RegAsm` 为基础；进程内 Add-in 保持 .NET Framework 4.8 目标，并直接使用本机 SOLIDWORKS 2026 的同版本 PIA。快速演进的 AI 和产品逻辑放在 .NET 10 Host 中，避免把网络、模型依赖和额外运行时风险注入 `SLDWORKS.exe`。

## 核心边界

- 所有 SOLIDWORKS COM 调用都在 Add-in 所属的串行 STA/UI 调度路径执行。
- COM 对象不得跨线程长期持有，也不得跨进程发送。
- Planner 输出版本化、白名单式 IR，不输出任意 API 调用。
- 每个计划携带 `documentId`、`documentRevision`、前置条件、后置条件和 `planHash`；revision 由 Add-in 的事件计数与执行前模型 fingerprint 共同校验。
- 预览后文档或选择发生变化时，旧计划自动失效。
- 用户确认后再次校验计划；Undo Object 只负责把可撤销操作分组，失败恢复还必须使用经真机验证的 Undo、补偿或执行前副本策略，并再次 rebuild 验证。
- 结果必须是原生 sketch、dimension、feature；Imported Body 不算成功。
- 线上失败样例脱敏后进入回归语料库。

## 重新实现路线

### M0：平台底座

- 建立新 Solution 与双进程骨架。
- Add-in 能加载/卸载且不影响 SOLIDWORKS 稳定性。
- Task Pane 显示 Host 连接状态和当前文档摘要。
- 完成 Named Pipe 握手、协议版本协商和断线恢复。
- 建立 COM 串行调度、结构化错误和日志。

### M1：确定性 CAD 闭环

- 用手写 IR 创建验证零件，例如矩形板、拉伸和四孔。
- 支持修改命名/稳定引用的尺寸。
- 执行前展示计划、差异、依赖和风险。
- 实现 revision 检查、Undo Object、重建与后置条件验证。
- 保存并重新打开 `.SLDPRT` 后，特征仍原生且可编辑。

验证零件只是工程测试夹具，不代表首个商业场景已经确定。

### M2：规则 Planner

- 用模板和规则覆盖一组窄、可测的用户意图。
- 处理单位、缺失参数、澄清问题和不支持的请求。
- Planner 只能生成与 M1 相同的协议对象。

### M3：LLM Planner

- 通过 provider adapter 接入模型，不把特定模型写死在协议中。
- 使用结构化输出、schema 校验、shadow mode 和 prompt 回归集。
- 先开放低风险 capability，再逐步扩展。

### M4：产品化

- 用户研究决定首个高价值工作流。
- 建立 SOLIDWORKS 2026 golden model 真机回归。
- 完善签名安装、更新、审计、企业数据策略和故障恢复。

## 当前仓库

```text
README.md                                      当前项目入口
Text2Cad.sln                                   当前新实现 Solution
text2cad_mvp_spec.md                           2026 MVP 技术规格
text2cad_product_plan.html                     2026 产品执行计划（单页）
text2cad_validation_guide.html                 2026 M0/M1 真机验证指南（单页）
src/Text2Cad.Addin/                            M0/M1 进程内 Add-in、对话 Task Pane 与基础几何命令
tests/Text2Cad.Addin.SmokeTests/                无许可证时可运行的路由与尺寸契约检查
scripts/Register-Text2CadAddin.cmd             开发机注册脚本
scripts/Unregister-Text2CadAddin.cmd           开发机卸载脚本
src/SwCopilot/                                 早期预研代码，仅供参考
deploy/                                        早期预研安装脚本，仅供参考
```

当前新实现从以下结构开始，并会在后续里程碑继续扩展：

```text
src/
  Text2Cad.Addin/       .NET Framework 4.8，唯一的 SOLIDWORKS COM 边界
  Text2Cad.Host/        .NET 10 LTS，Planner 与产品逻辑（待创建）
  Text2Cad.Protocol/    跨进程 DTO 与 schema（待创建）
tests/
  Text2Cad.Addin.SmokeTests/   net48 离线路由与尺寸契约检查
  Text2Cad.UnitTests/          （待创建）
  Text2Cad.ContractTests/      （待创建）
  Text2Cad.IntegrationTests/   （待创建）
research/
  legacy/               旧代码和历史验证材料
```

在新 Solution 建立前，不要按照 `src/SwCopilot/` 或 `deploy/` 中的旧脚本安装插件。

## 开发机准备

1. Windows 11 x64。
2. SOLIDWORKS 2026 x64，包含 API SDK，并完成授权。
3. Visual Studio Community 2026，安装 `.NET 桌面开发` workload。
4. 确认安装 `.NET Framework 4.8 SDK/Targeting Pack` 与 `.NET 10 SDK`。
5. 使用当前 SOLIDWORKS 2026 安装目录中的同版本 API SDK/PIA，并在真机回归中记录 Interop 来源、SOLIDWORKS service pack 和文件版本。不要混用来历不明的 DLL，也不要提交厂商 DLL。
6. 所有几何集成测试都在专用测试零件和测试配置上运行，不在重要生产模型上试验。

## M0/M1 Panel 构建与真机加载

1. 在 Visual Studio 中打开 `Text2Cad.sln`，选择 `Debug | x64` 并构建。
2. 保存工作并关闭 SOLIDWORKS。
3. 在管理员命令提示符中运行 `scripts\Register-Text2CadAddin.cmd`。
4. 正常启动 SOLIDWORKS，在 `工具 > 插件` 中手动勾选 `Text2CAD`。首次注册时默认不启用随 SOLIDWORKS 自动加载；后续使用注册脚本更新时保留用户现有设置，不会强制开启启动加载。稳定后可自行勾选启动列。
5. 确认右侧 Task Pane 显示连接状态、当前文档、对话记录、文字输入框和三个固定能力入口。
6. 输入并发送 `创建一个 100 × 60 × 10 mm 测试板`，确认出现新的未保存零件，特征树包含 `Text2CAD_TestPlate_Sketch` 和 `Text2CAD_TestPlate_100x60x10`。
7. 分别发送 `创建一个边长 40 mm 的方块` 和 `创建一个直径 40 mm、高 60 mm 的圆柱`，确认各自新建独立 Part，并收到成功消息。

当前 Panel 已具备最小完整对话体验：文字输入、Enter 发送、消息记录、执行中状态、成功/失败反馈和三个示例入口。`ChatCommandRouter` 只做确定性关键词匹配，不接 LLM；三个能力分别创建 `100 × 60 × 10 mm` 测试板、`40 × 40 × 40 mm` 方块和 `Ø40 × 60 mm` 圆柱。每次命令都基于模板新建独立 Part，不会回退到当前已有文档；失败时只关闭本次新建的半成品。实现与排错细节见 [`src/Text2Cad.Addin/README.md`](src/Text2Cad.Addin/README.md)。

2026-07-17 已在本机 SOLIDWORKS 2026 SP3.2 真机验证：启用自动加载时 Add-in 可正常显示 Text2CAD Task Pane；测试板、方块和圆柱分别新建独立 `swDocPART`，均包含 1 个实体和对应的原生拉伸特征。为避免启动崩溃循环，当前首次注册默认采用手动加载。Debug/Release 构建与两个配置的 52 项 smoke checks 全部通过。这仍是 API 连通性夹具，不等同于完整的 Execution Plan、Undo/恢复或 AI Planner。

历史上 2026-07-14 曾在 SOLIDWORKS 2025 SP0.0 完成测试板 API 探针，但 SOLIDWORKS 2025 不再属于 MVP 支持范围。2026-07-15 因许可证过期记录的三条对话回归 `BLOCKED` 状态，已由上述 2026 SP3.2 回归解除。

无有效 SOLIDWORKS 许可证时，构建 Solution 后仍可运行：

```powershell
.\tests\Text2Cad.Addin.SmokeTests\bin\x64\Debug\net48\Text2Cad.Addin.SmokeTests.exe
```

预期输出为 `PASS: 52 checks completed.`；测试边界见 [`tests/Text2Cad.Addin.SmokeTests/README.md`](tests/Text2Cad.Addin.SmokeTests/README.md)。

详细的环境与验收步骤见 [`text2cad_validation_guide.html`](text2cad_validation_guide.html)。技术契约见 [`text2cad_mvp_spec.md`](text2cad_mvp_spec.md)，产品路线见 [`text2cad_product_plan.html`](text2cad_product_plan.html)。

## 官方参考

- [Visual Studio 2026 下载](https://visualstudio.microsoft.com/downloads/)
- [Visual Studio 2026 平台兼容性](https://learn.microsoft.com/en-us/visualstudio/releases/2026/compatibility)
- [SOLIDWORKS 系统要求](https://www.solidworks.com/support/system-requirements)
- [SOLIDWORKS 2026 API Help](https://help.solidworks.com/2026/english/api/sldworksapiprogguide/welcome.htm)
- [SOLIDWORKS C# Add-in 开发入口](https://help.solidworks.com/2026/english/api/sldworksapiprogguide/GettingStarted/Visual_C__Standalone_and_Add-in_Applications.htm)
- [.NET Framework 版本兼容性](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/version-compatibility)
- [.NET 版本支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy)
