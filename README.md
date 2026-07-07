# SolidWorks Copilot MVP

一个嵌在 SolidWorks 里的聊天式插件原型:在 Task Pane 输入自然语言 → 硬编码路由 →
显示执行计划 → 确认 → 本地 executor 调 SolidWorks API 创建/修改**原生特征树**。

> 定位:**"假装智能、真实执行"**。不接 LLM,用确定性路由;重点是证明 Copilot 式 UI 能安全触发原生 SW 操作。
> 详细规格见 [`solidworks_addin_mvp_spec.md`](../solidworks_addin_mvp_spec.md)。

支持三条命令:

| 输入示例 | 动作 |
|---|---|
| `创建一个 100x60x8 的安装板，四个 M6 通孔` | 创建 `AI_BasePlate` + `AI_4x_M6_ThroughHoles` |
| `把厚度改成 10mm` | 改 `AI_thickness`,重建 |
| `把孔径改成 8mm` | 改 `AI_hole_diameter`,重建 |

---

## ⚠️ 先读:两个硬前提

1. **只能在 Windows + 已安装 SolidWorks 的机器上编译和运行。** 这是 .NET Framework COM 插件,不能在 macOS/Linux 上跑。
2. **运行 demo 的那台机器必须已经装好并激活 SolidWorks(建议 2025,与编译用的 interop 版本一致)。** 插件不是独立软件,它挂在 SolidWorks 里。

> 本仓库的代码**尚未在真实 SolidWorks 上验证过**。首次演示前,请务必在一台装了 SolidWorks 2025 的机器上按下面步骤 build + 跑一遍。

---

## 目录结构

```
src/
  SwCopilot.sln
  SwCopilot/
    SwAddin.cs                 插件入口 (ISwAddin) + COM 注册 + Task Pane
    UI/CopilotControl.cs       聊天 UI (状态 / transcript / 输入 / 计划 / 执行)
    Routing/                   CommandTypes.cs, CommandRouter.cs  (硬编码路由)
    Planning/PlanBuilder.cs    命令 -> 中文执行计划
    Execution/                 ExecutionResult.cs, SwApiHelpers.cs, Executor.cs
    third-party/               (放 SW2025 interop DLL,见下)
deploy/
  install.bat / uninstall.bat  regasm /codebase 注册 (自动提权)
  SwCopilot.iss                (可选) Inno Setup 打包成 Setup.exe
```

---

## 构建(在 Windows 上)

### 1. 环境
- Visual Studio 2022(社区版即可),勾选 **.NET desktop development**
- **.NET Framework 4.8 Developer Pack**
- SolidWorks 2025(用于取 interop DLL,以及测试)

### 2. 放入 SolidWorks interop DLL
从 SolidWorks 安装目录复制这三个文件:

```
C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\
    SolidWorks.Interop.sldworks.dll
    SolidWorks.Interop.swconst.dll
    SolidWorks.Interop.swpublished.dll
```

到:

```
src\SwCopilot\third-party\
```

(`.csproj` 已用 HintPath 指向这里,`Copy Local = False`。)

### 3. 编译
打开 `src\SwCopilot.sln`,把配置切到 **Release / x64**,Build。
输出:`src\SwCopilot\bin\Release\SwCopilot.dll`。

---

## 安装 / 注册

**方式 A(推荐,零额外工具):**
以**管理员**身份运行 `deploy\install.bat`(双击后会自动请求提权)。
它会对 `SwCopilot.dll` 执行 `RegAsm.exe /codebase`,写入 SolidWorks 需要的注册表键。

**方式 B(打包给同事):**
把 `SwCopilot.dll` 和 `install.bat` 放到同一个文件夹发给同事,让他右键 `install.bat` → 以管理员运行。

**方式 C(可选 Setup.exe):**
装 [Inno Setup 6](https://jrsoftware.org/isdl.php),在 `deploy\` 下运行 `iscc SwCopilot.iss`,生成 `SwCopilot-Setup.exe`。

卸载:`deploy\uninstall.bat`(管理员)。

---

## 运行 & 演示

1. 打开 SolidWorks 2025。
2. `工具 (Tools) > 插件 (Add-Ins)`,勾选 **SolidWorks Copilot**(启动/激活都勾上)。
3. 右侧 Task Pane 会出现 Copilot 图标,点开。
4. 依次演示:

   - **Flow A**:输入 `创建一个 100x60x8 的安装板，四个 M6 通孔` → 看计划 → 点 `执行`。
     特征树应出现 `AI_BasePlate` 与 `AI_4x_M6_ThroughHoles`(**原生特征,非 Imported body**)。
   - **Flow B**:输入 `把厚度改成 10mm` → `执行`。厚度更新并重建。
   - **Flow C**:输入 `把孔径改成 8mm` → `执行`。四个孔直径更新并重建。
   - **不支持的输入**:随便输 → 返回"当前 MVP 只支持…"三条提示。

> 若没有打开零件,`创建安装板` 会自动新建一个零件(可在 `Executor.AutoCreatePartIfNone` 关闭)。

---

## 排错

| 现象 | 处理 |
|---|---|
| 插件列表里没有 "SolidWorks Copilot" | 确认 `install.bat` 以管理员运行且成功;确认 DLL 是 **x64 Release**;确认 SolidWorks 位数与 DLL 一致(都是 64 位) |
| RegAsm 报 "unsigned assembly /codebase" 警告 | 开发阶段正常,可忽略 |
| 加载时弹错 / interop 版本不符 | 同事机器的 SolidWorks 若不是 2025,请用**他那个版本**的 `api\redist` interop 重新编译 |
| `无法自动新建零件(缺少默认零件模板)` | 在 SolidWorks 里先手动新建一个零件,或设置默认零件模板 |
| 孔/基准面/切除相关报错 | 见下"已知易碎点",按报错在真机微调 |

---

## 已知易碎点(无测试机导致,首次真机跑重点看这些)

代码已按 spec §10 做了防御,但下面几处最可能需要在真实几何上微调:

- **命名尺寸**:`AI_thickness`(拉伸深度)最稳;`AI_hole_diameter` 依赖对草图圆 `AddDimension2` 加直径尺寸——若某版本行为不同,Flow C 可能需要调整(见 `SwApiHelpers.AddNamedDimension`)。
- **切除方向**:已用"双向 Through-All"规避方向问题(`Executor.CreateMountingPlate` 里的 `FeatureCut4`)。
- **基准面本地化**:已尝试中英文名 + 按类型遍历兜底(`SwApiHelpers.SelectFrontPlane`)。

所有 SW 调用都包了 try/catch,失败会在 Task Pane 显示原因而不崩溃 SolidWorks。把报错文本贴回来即可继续迭代。

---

## 不在范围内(MVP)

LLM、云端、登录、计费、装配体、工程图、通用自然语言理解、生产级安装器加固、撤销栈。
下一步方向见产品计划 `solidworks_copilot_product_plan.html`。
