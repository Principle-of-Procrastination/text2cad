# Text2Cad.Addin.SmokeTests

这是一个零 NuGet 依赖的 `net48` Console smoke-test 项目，用于在 SOLIDWORKS 无法启动或没有有效许可证时验证确定性代码契约。

项目通过 `<Compile Link>` 直接编译产品中的：

- `Commands/ChatCommandRouter.cs`；
- `Commands/PrimitiveCatalog.cs`。

因此测试覆盖的是与 Add-in 完全相同的路由和尺寸定义，同时不加载 SOLIDWORKS PIA、不创建 COM 对象，也不需要运行 `SLDWORKS.exe`。

## 覆盖范围

- 测试板、方块和圆柱的中英文关键词路由；
- 帮助、未知输入、空输入和多形状输入不会执行 CAD；
- 三个基础几何的米制草图尺寸与拉伸深度；
- 草图/拉伸特征名称契约；
- 未知 `PrimitiveKind` 被拒绝。

当前共执行 52 项检查。

## 运行

先在 Visual Studio 中构建 `Text2Cad.sln` 的 `Debug | x64`，再运行：

```powershell
.\tests\Text2Cad.Addin.SmokeTests\bin\x64\Debug\net48\Text2Cad.Addin.SmokeTests.exe
```

成功时输出：

```text
PASS: 52 checks completed.
```

这些检查不能替代 SOLIDWORKS 真机回归；模板解析、COM 生命周期、草图、拉伸、FeatureManager 和 Task Pane 布局仍必须在有效许可证环境中验证。
