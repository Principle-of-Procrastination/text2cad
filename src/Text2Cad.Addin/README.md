# Text2Cad.Addin

这是 Text2CAD 的新进程内 SOLIDWORKS Add-in，不依赖旧 `src/SwCopilot` 实现。

当前 M0/M1 切片负责：

- 实现 `ISwAddin` 生命周期；
- 创建 64 位 SOLIDWORKS Task Pane；
- 显示连接状态、当前文档和选择数量；
- 提供文字输入、Enter 发送、消息记录、执行状态和三个示例入口；
- 用简单关键词把输入确定性路由到测试板、方块或圆柱，不执行真正的意图识别；
- 始终新建独立 Part，并在新文档中创建原生矩形/圆形草图和盲孔式凸台拉伸；
- 用 COM identity 校验建模目标仍是本次新建文档，不允许回退到用户原有模型；
- 在失败时关闭本次新建的半成品，不关闭或撤销用户原有文档；
- 把诊断日志写入 `%LOCALAPPDATA%\Text2CAD\logs\addin.log`。

固定路由位于 `Commands/ChatCommandRouter.cs`，尺寸契约位于 `Commands/PrimitiveCatalog.cs`，建模管线位于 `Commands/CreatePrimitiveCommand.cs`：

| 输入关键词 | 固定结果 | 草图轮廓 | 拉伸深度 |
|---|---|---|---|
| `测试板`、`安装板`、`plate` | 100 × 60 × 10 mm 测试板 | 100 × 60 mm 矩形 | 10 mm |
| `方块`、`立方体`、`cube` | 40 × 40 × 40 mm 方块 | 40 × 40 mm 矩形 | 40 mm |
| `圆柱`、`cylinder`、`直径40` | Ø40 × 60 mm 圆柱 | Ø40 mm 圆 | 60 mm |

SOLIDWORKS API 长度统一使用米。基准面通过内部特征类型 `RefPlane` 查找，不依赖 `Front Plane`、`前视基准面` 等本地化名称。多形状输入和未知输入只返回说明，不新建文档。

零件模板按以下顺序解析：

1. `swDefaultTemplatePart` 显式默认模板；
2. `ISldWorks.GetDocumentTemplate(swDocPART, ...)`；
3. 当前 SOLIDWORKS 主版本在 `%PROGRAMDATA%\SolidWorks\SOLIDWORKS <year>\templates` 下安装的 `.prtdot`，优先 `gb_part.prtdot` 和 `Part.prtdot`。

## 构建

在 Visual Studio 2026 中打开根目录的 `Text2Cad.sln`，选择 `Debug | x64` 后构建。

项目从以下注册表位置自动读取 SOLIDWORKS 2025 安装目录：

```text
HKLM\SOFTWARE\SolidWorks\SOLIDWORKS 2025\Setup
  SolidWorks Folder
```

如需覆盖，可向 MSBuild 传入 `SolidWorksInstallDir`。

## 注册与验证

1. 关闭 SOLIDWORKS。
2. 以管理员身份打开命令提示符。
3. 运行 `scripts\Register-Text2CadAddin.cmd`。
4. 正常启动 SOLIDWORKS；Text2CAD 默认随 SOLIDWORKS 自动加载。必要时打开 `工具 > 插件` 手动勾选。
5. 确认右侧 Task Pane 出现 Text2CAD 标签，并显示对话记录、输入框和三个示例按钮。
6. 输入 `创建一个 100 × 60 × 10 mm 测试板` 后按 Enter；成功时应新建一个零件并显示绿色成功消息。
7. 分别发送 `创建一个边长 40 mm 的方块` 和 `创建一个直径 40 mm、高 60 mm 的圆柱`；每条消息都应新建独立 Part，并生成原生草图和拉伸特征。
8. 输入 `做一个球`，确认 Panel 只列出当前支持能力且不创建文档。

## 2025 真机验收记录

2026-07-14 在 SOLIDWORKS 2025 SP0.0 验证通过：

- 新建文档：`零件1`，类型 `swDocPART`；
- 草图编辑状态：已退出；
- 原生特征：`Text2CAD_TestPlate_Sketch | ProfileFeature`；
- 原生特征：`Text2CAD_TestPlate_100x60x10 | Extrusion`；
- 可见实体：1；
- 实体包围盒：`100 × 60 × 10 mm`；
- 构建结果：0 个警告、0 个错误。

2026-07-15 已完成：

- 对话 UI 和固定三能力路由实现；
- 无 NuGet/PIA 运行依赖的 smoke-test 项目；
- 52 项路由、米制尺寸和特征名契约检查在 Debug/Release 下通过；
- `Debug | x64` 与 `Release | x64` 编译通过；
- 本机 SOLIDWORKS 许可证已过期，三条新对话的真机回归当前为 `BLOCKED`。

卸载时关闭 SOLIDWORKS，并在管理员命令提示符中运行 `scripts\Unregister-Text2CadAddin.cmd`。
