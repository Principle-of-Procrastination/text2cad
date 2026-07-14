# Text2CAD 2026 MVP 技术规格

- 状态：Draft / 新实现基线
- 更新日期：2026-07-14
- 目标 CAD：SOLIDWORKS 2025 x64（最低基线）；SOLIDWORKS 2026 x64（兼容验证）

## 1. 目的

MVP 要验证的不是“LLM 会不会调用 CAD API”，而是下面这个闭环能否安全、可重复地工作：

```text
自然语言或手写意图
→ 当前文档与选择快照
→ 版本化语义计划
→ 本地 capability/参数/revision 校验
→ 用户预览并确认 plan hash
→ 确定性 SOLIDWORKS Executor
→ Rebuild 与后置条件验证
→ 成功提交或失败恢复
→ 原生、可继续编辑的特征树
```

MVP 成功的标准不是生成一个看起来正确的形状，而是以可控方式修改真实 SOLIDWORKS 文档，同时保留设计意图、可编辑性和恢复能力。

## 2. 平台基线

### 2.1 开发与运行环境

- Windows 11 x64。
- SOLIDWORKS 2025 x64 与同版本 API SDK/Primary Interop Assemblies，作为最低 API 基线。
- SOLIDWORKS 2026 x64 兼容验证环境。
- Visual Studio Community 2026。
- Add-in：C#、.NET Framework 4.8、x64。
- Host：C#、.NET 10 LTS、x64。

### 2.2 为什么采用双运行时

SOLIDWORKS 2025 和 2026 的官方 C# Add-in 仍通过 .NET Framework PIA、COM、`ISwAddin` 和 `RegAsm` 加载。Add-in 以 .NET Framework 4.8 为共同目标，只使用 SOLIDWORKS 2025 已提供的 API 表面；同一实现必须在 2025 基线和 2026 兼容环境中分别验证。Add-in 必须足够薄、稳定，并与 SOLIDWORKS 的线程和生命周期一致。

LLM、网络、数据库、检索、会话和快速演进的依赖放入独立 .NET 10 Host。这样 Host 崩溃、升级或超时时，不会直接拖垮 `SLDWORKS.exe`。

## 3. 进程与模块边界

```text
SLDWORKS.exe
└─ Text2Cad.Addin (.NET Framework 4.8, x64)
   ├─ SwHost
   ├─ TaskPaneHost
   ├─ ContextCollector
   ├─ CadDispatcher
   ├─ PlanValidator
   ├─ DeterministicExecutor
   ├─ ExecutionVerifier
   └─ BrokerClient
          ↕ localhost Named Pipe / versioned DTO
Text2Cad.Host.exe (.NET 10 LTS, x64)
├─ SessionOrchestrator
├─ Planner
├─ ModelProvider adapters
├─ Schema and policy validation
├─ Audit and diagnostics
└─ BrokerServer
```

### 3.1 Add-in 职责

- 实现 `ISwAddin.ConnectToSW` / `DisconnectFromSW`。
- 创建 Task Pane、订阅必要的文档/选择事件。
- 生成只读 `DocumentSnapshot` 与 `SelectionSnapshot`。
- 将所有 SOLIDWORKS COM 调用串行调度到所属 STA/UI 路径。
- 对 Host 返回的计划执行最终 schema、capability、revision、引用和参数校验。
- 展示预览、风险和确认状态。
- 仅执行白名单 capability，并验证 rebuild 与后置条件。
- 将失败转换为结构化结果；不得向 UI 泄漏未处理 COM 异常。

### 3.2 Host 职责

- 管理会话、上下文压缩和澄清问题。
- 通过可替换 provider 调用 LLM。
- 把意图编译为版本化 `ExecutionPlan`。
- 执行第一层 schema、业务规则与策略校验。
- 保存审计、诊断和脱敏回归样例。
- 处理超时、取消、Host 重启和协议版本协商。

### 3.3 严格禁止

- Host 或后台线程直接调用 SOLIDWORKS COM API。
- 跨进程传输 COM 对象、RCW、IUnknown、窗口指针或任意可执行代码。
- 让模型输出或执行 C#、VBA、宏、PowerShell 或原始 SOLIDWORKS API 调用。
- 仅凭 FeatureManager 显示名称定位关键几何对象。
- 吞掉异常后把部分失败报告成成功。

## 4. 线程模型

- Add-in 内部只有 `CadDispatcher` 可以接触 SOLIDWORKS API。
- CAD 操作按计划串行执行；后台任务只能处理纯 DTO。
- COM 对象的生命周期不得超过其必要作用域。
- 取消请求只在原子 capability 之间生效；不得强行终止正在执行的 COM 调用。
- UI 不直接承担长时间网络和模型推理。

## 5. IPC

MVP 使用本机 Named Pipe：

- 只允许当前 Windows 用户访问。
- 每次启动生成随机会话令牌。
- Add-in 与 Host 做协议版本、产品版本和 capability 协商。
- 消息必须包含 `requestId`、`correlationId` 和超时。
- 重复请求通过幂等键处理。
- Host 断开时 Add-in 保持稳定，禁用规划但保留诊断和重连入口。

MVP 消息类型：

```text
Hello / HelloAck
DocumentSnapshot
SelectionSnapshot
PlanRequest
ClarificationRequired
ExecutionPlan
ConfirmPlan
ExecutePlan
CancelRequest
Progress
ExecutionResult
ErrorEnvelope
```

## 6. 文档快照

`DocumentSnapshot` 至少包含：

```json
{
  "schemaVersion": 1,
  "documentId": "...",
  "documentRevision": "...",
  "documentType": "Part",
  "activeConfiguration": "Default",
  "unitSystem": "MMGS",
  "featureGraph": [],
  "dimensions": [],
  "selection": [],
  "rebuildStatus": "Clean"
}
```

要求：

- 快照是最小必要摘要，不默认上传完整 CAD 文件。
- 选择对象使用 Add-in 生成的稳定 token；可用时基于 SOLIDWORKS Persistent Reference ID。
- token 同时携带对象类型、文档身份和预期拓扑信息。
- `documentRevision` 不是直接读取某个通用 SOLIDWORKS 字段。MVP 由 Add-in 为每个文档维护单调事件计数，并对会影响计划的状态生成 fingerprint；执行前重新采集 fingerprint。事件遗漏或 fingerprint 不一致时按 stale plan 拒绝执行。

## 7. 受控 Execution Plan

### 7.1 顶层结构

```json
{
  "schemaVersion": 1,
  "requestId": "...",
  "documentId": "...",
  "documentRevision": "...",
  "units": "mm",
  "preconditions": [],
  "operations": [],
  "postconditions": [],
  "risk": "low",
  "planHash": "..."
}
```

### 7.2 设计原则

- Operation 是产品语义 capability，不是底层 API 函数名。
- capability registry 决定本机当前可以执行哪些操作。
- 未知字段、未知 operation、非法枚举、非有限数、越界尺寸或跨文档引用全部拒绝。
- 所有长度、角度和容差都显式携带单位。
- `planHash` 使用规范化 JSON 计算：对象键排序、数值和单位规范化、数组顺序保持；哈希输入排除 `planHash` 本身、签名和 IPC 传输元数据。用户确认后，参与哈希的计划内容不可变。

### 7.3 M1 capability

```text
CreateExtrudedPlate
CreateHolePattern
UpdateDimension
RebuildDocument
```

Executor 可以把语义 capability 展开为草图、约束、尺寸、拉伸和切除等确定步骤，但该展开过程完全由本地代码控制。

### 7.4 示例

```json
{
  "schemaVersion": 1,
  "requestId": "m1-create-plate-001",
  "documentId": "part:demo",
  "documentRevision": "rev-12",
  "units": "mm",
  "operations": [
    {
      "type": "CreateExtrudedPlate",
      "width": 100,
      "height": 60,
      "depth": 8
    },
    {
      "type": "CreateHolePattern",
      "diameter": 6,
      "count": 4,
      "edgeOffsetX": 10,
      "edgeOffsetY": 10
    }
  ],
  "postconditions": [
    "rebuildHasNoErrors",
    "nativeFeaturesAreEditable"
  ],
  "risk": "low",
  "planHash": "sha256:..."
}
```

安装板只是 M1 的 golden fixture，不是已经确认的市场 MVP。

## 8. 计划预览与确认

Task Pane 必须显示：

- 当前文档与 configuration。
- Host 连接和协议状态。
- 用户请求及必要的澄清问题。
- 将新增、修改或删除的对象。
- 参数和单位的 before/after。
- 前置条件、后置条件、风险和不可恢复警告。
- `planHash` 对应的确认状态。

确认流程：

1. Add-in 收到计划后进行最终校验。
2. 生成只读差异预览，不修改正式特征树。
3. 用户确认当前 `planHash`。
4. 执行前重新读取 `documentRevision` 和选择引用。
5. 任一状态变化都会使确认失效并要求重新规划。

## 9. 执行与恢复

```text
Validate
→ Start Undo Object
→ Execute capability 1..n
→ Finish Undo Object
→ Rebuild
→ Verify postconditions
→ Success，或执行经验证的 EditUndo2/补偿/副本恢复
→ 再次 Rebuild 并验证恢复结果
```

失败策略：

- 每一步返回结构化状态、API 证据和可操作错误。
- MVP 使用 `StartRecordingUndoObject` / `FinishRecordingUndoObject2` 把 SOLIDWORKS 支持撤销的一组操作合并为单一撤销项；它本身不会自动回滚。
- 失败后只在经过对应 capability 真机验证时调用 `EditUndo2(1)`；不支持或无法确认的操作使用补偿或执行前副本策略。
- 对高风险操作增加执行前保存、临时副本或补偿操作。
- 回滚后再次重建并验证执行前状态。
- 如果无法证明恢复成功，停止后续执行并明确标记文档需要人工检查。
- 不宣称具有数据库式绝对原子性。

## 10. 结构化结果

```json
{
  "success": false,
  "requestId": "...",
  "planHash": "...",
  "completedOperations": [],
  "createdObjects": [],
  "modifiedObjects": [],
  "rebuildStatus": "Failed",
  "rollbackStatus": "Succeeded",
  "error": {
    "code": "SW_REBUILD_FAILED",
    "message": "模型重建失败",
    "operationIndex": 1,
    "details": "..."
  }
}
```

`success=true` 只在所有 operation、rebuild 和后置条件均通过时返回。

## 11. MVP 用户流程

### Flow A：连接与快照

1. 用户启用 Add-in。
2. Task Pane 显示 Add-in、Host、协议和文档状态。
3. 打开或新建 Part。
4. Add-in 生成快照，不修改文档。

### Flow B：手写计划创建 golden fixture

1. 测试工具提交固定计划，由当前实现按规范化规则计算并校验哈希。
2. UI 展示矩形板、拉伸和四孔预览。
3. 用户确认。
4. Executor 创建原生特征并重建。
5. 保存、关闭、重新打开后仍可 Edit Sketch / Edit Feature。

### Flow C：增量修改

1. 对现有 fixture 生成修改厚度或孔径的计划。
2. UI 展示 before/after。
3. 确认后修改稳定引用的尺寸并重建。

### Flow D：拒绝与恢复

- 未知 capability、非法单位、零/负尺寸、越界几何被拒绝且不触达 COM。
- 预览后文档变化使计划失效。
- 中间步骤失败时文档恢复到执行前可用状态。
- Host 被终止时 SOLIDWORKS 继续稳定运行。

## 12. 测试策略

### 12.1 纯逻辑测试

- JSON schema 和版本兼容。
- 单位、数值范围、哈希规范化和 capability 校验。
- Planner golden cases、结构化输出和澄清策略。
- 随机/模糊输入不得绕过 validator。

### 12.2 Adapter 契约测试

- 使用自建接口隔离 COM，测试线程调度、单位转换、revision、错误映射和补偿顺序。
- Mock 不能替代真实几何验证。

### 12.3 真机集成测试

- 专用 Windows 11 + SOLIDWORKS 2025 基线 runner。
- 独立的 Windows 11 + SOLIDWORKS 2026 兼容 runner，运行同一套 M0/M1、负向和恢复用例。
- 固定模板、语言、单位、configuration 和 golden models。
- 执行后保存并重开，断言特征类型、尺寸、质量属性、rebuild 错误和 Undo 状态。
- 两个版本都记录 service pack、Interop 来源和文件版本；后续按产品支持范围扩展 service pack 兼容矩阵。

## 13. MVP 验收标准

- Add-in 可以反复加载/卸载，不遗留崩溃或僵死进程。
- Host 完全离线或崩溃时，SOLIDWORKS 仍稳定可用。
- 非法或恶意计划无法触达 SOLIDWORKS COM Executor。
- 同一手写计划在固定环境中重复产生等价的原生特征树。
- 计划预览后文档变化会阻止执行。
- 用户确认的是不可变 `planHash`。
- 创建和修改操作在保存、重开后仍可编辑并成功 rebuild。
- 任一步失败都返回明确错误；能够验证成功恢复，或明确要求人工检查。
- 全链路具有 `requestId`、`planHash` 和操作级日志。

## 14. MVP 非目标

- 任意零件或装配体的通用自然语言生成。
- LLM 直接控制 API 或生成并执行代码。
- 生产级多 CAD 支持。
- PDM/PLM、SSO、计费和完整企业部署。
- 在 MVP 阶段承诺绝对事务或全自动拓扑引用稳定性。
- 以安装板 demo 代替真实用户研究。

## 15. 阶段门

```text
M0 平台底座
  Gate：Add-in/Host/IPC/快照稳定
M1 确定性闭环
  Gate：preview + confirm + execute + verify + recover
M2 规则 Planner
  Gate：窄意图集可重复、可澄清
M3 LLM Planner
  Gate：shadow mode 指标达到阈值后才开放低风险执行
M4 产品化
  Gate：用户价值、兼容矩阵、部署和审计成立
```

## 16. 官方参考

- [SOLIDWORKS 2025 API Help](https://help.solidworks.com/2025/english/api/sldworksapiprogguide/welcome.htm)
- [SOLIDWORKS 2026 API Help](https://help.solidworks.com/2026/english/api/sldworksapiprogguide/welcome.htm)
- [SOLIDWORKS C# Add-in Applications](https://help.solidworks.com/2025/english/api/sldworksapiprogguide/GettingStarted/Visual_C__Standalone_and_Add-in_Applications.htm)
- [ISwAddin 与注册要求](https://help.solidworks.com/2025/English/api/sldworksapiprogguide/Overview/Using_SwAddin_to_Create_a_SolidWorks_Addin.htm)
- [Persistent Reference IDs](https://help.solidworks.com/2025/English/api/sldworksapiprogguide/Overview/Persistent_Reference_IDs.htm)
- [StartRecordingUndoObject](https://help.solidworks.com/2025/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModelDocExtension~StartRecordingUndoObject.html)
- [FinishRecordingUndoObject2](https://help.solidworks.com/2025/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModelDocExtension~FinishRecordingUndoObject2.html)
- [EditUndo2](https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDoc2~EditUndo2.html)
- [.NET Framework 版本兼容性](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/version-compatibility)
- [Visual Studio 2026 兼容性](https://learn.microsoft.com/en-us/visualstudio/releases/2026/compatibility)
- [.NET 10 支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy)
