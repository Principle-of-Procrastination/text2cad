# Text2CAD 跨 App 后续执行规划

- 状态：Proposed
- 更新日期：2026-07-19
- 当前基线：SOLIDWORKS 2026 Add-in 已完成 UI 与三条确定性建模命令验证

## 1. 目标

建立一套可复用的跨 CAD 架构：

- UI 使用同一套 WebView 前端。
- 本地数据与通用业务逻辑由 C# 统一实现。
- 使用统一、版本化的 CAD DSL 描述跨 App 建模意图。
- 每个 CAD App 使用独立的 C# Bridge 对接厂商 API。
- 独立部署的 Server 负责用户管理与 AI Agent。

实施顺序保持克制：先完成 SOLIDWORKS 2026 的新架构闭环，再选择一个 CAD App 验证跨 App 能力，最后逐个扩展。

迁移说明：当前待建的 `Text2Cad.Host` 按本计划收敛为 `Text2Cad.Local`，本地只保留数据和通用流程；AI Planner 与模型调用移到 Server。现有 `Text2Cad.Addin` 在 P3 前继续作为 SOLIDWORKS 2026 的可运行基线。

## 2. 目标架构

```text
CAD App 进程
└─ C# Plugin
   ├─ WebView2 Host ── 加载同一套 Text2Cad.Web
   ├─ App Bridge ───── DSL Compiler + 当前 CAD API
   └─ Local Client ─── 版本化 IPC
             │
             ▼
Text2Cad.Local.exe（C#，每个 Windows 用户单实例）
├─ 会话、消息、设置与缓存
├─ 本地数据库、迁移与恢复
├─ DSL 解析、校验、规范化与 planHash
├─ Server Client 与安全 Token 缓存
├─ App 实例、文档和 capability 编排
└─ 日志、超时、取消与错误归一化
             │ HTTPS + Streaming
             ▼
Text2Cad.Server
├─ 用户、认证与授权
├─ AI Agent、模型调用与候选 DSL 生成
├─ Agent 工具和策略编排
└─ 必要的审计、限流与可观测性
```

WebView2 是 Windows 客户端的具体承载方式。首版前端资源随插件本地发布并锁定版本，不从 Server 动态下发。

## 3. 模块职责

| 模块 | 负责 | 不负责 |
|---|---|---|
| `Text2Cad.Web` | 页面渲染、用户输入、消息流、状态和确认界面 | 持久化、直接调用 CAD API、直接访问 Server |
| `Text2Cad.Local` | 会话存储、设置、Token 安全缓存、DSL 校验与确认状态、Server 通信、Bridge 调度 | UI 渲染、厂商 API、AI 推理 |
| `Text2Cad.Dsl` | 统一 CAD 语义、语言无关 schema、C# / TypeScript 生成类型、规范化、哈希规则、基础校验和编译器接口 | 厂商 API 映射、进程通信、Agent |
| `Text2Cad.Bridges.*` | 插件生命周期、上下文快照、线程调度、capability 声明、DSL 编译、执行、验证与恢复 | 会话库、用户系统、AI Agent |
| `Text2Cad.Server` | 用户管理、认证授权、Agent 编排、模型调用、流式响应和必要审计 | 加载 CAD SDK、直接访问或控制本地 CAD |
| `Text2Cad.Contracts` | UI、Local、Bridge、Server 之间的握手、传输信封、流式事件和错误模型 | CAD 语义和具体业务实现 |

### 数据归属

- 会话正文、消息、设置和本地执行记录以本地数据库为准。
- 用户、认证、授权和 Agent 运行记录以 Server 为准。
- CAD 文档和几何状态始终以当前 CAD App 为准。
- Server 默认只接收 Agent 所需的最小上下文；MVP 不上传完整 CAD 文件，也不提供会话云同步。

## 4. 核心协议

所有边界只传版本化数据，不传 COM 对象、厂商 API 对象、窗口指针或可执行代码。

### Web UI ↔ Local

- 初始化、登录状态和运行环境。
- 会话创建、查询、切换、重命名和删除。
- 消息发送、流式响应、取消、错误和重试。
- 当前 App、文档、执行进度和用户确认。

WebView 通过 C# Plugin 转发消息，不直接持有 Server Token。

### Local ↔ App Bridge

- App、版本、实例和当前文档信息。
- 选择集与最小上下文快照。
- capability 列表与版本协商。
- DSL 的 App 级校验与预览。
- `execute`、`cancel`、`recover` 和统一结果。

共享协议描述语义能力，不假设各 CAD API 完全等价。恢复能力必须明确声明为 `verifiedUndo`、`compensation` 或 `restoreCopy`，不能假设所有 CAD 都支持可靠 Undo。不支持的能力必须明确拒绝；App 专属能力只能使用命名空间化、显式版本化的 operation/capability argument schema 表达，未声明字段仍然拒绝。

### Local ↔ Server

- 登录、Token 刷新、退出和授权状态。
- 创建、继续、取消 Agent Run。
- 上报当前 capability 与最小必要上下文。
- 接收文本增量、候选 DSL 和最终结果。

Server Agent 只能生成受约束的候选 DSL。DSL 必须经过本地校验、App Bridge 预览和用户确认，最终只能由当前 App Bridge 编译并执行。

## 5. 统一 CAD DSL

`Text2CAD DSL` 是统一规范名称，`ExecutionPlan` 是其顶层文档类型，“语义 IR”是对它的架构描述。三者指向同一套模型，不再新增并行计划格式。首版采用版本化 JSON AST 与 JSON Schema，而不是自由文本脚本。

### 核心结构

```text
ExecutionPlan
├─ schemaVersion
├─ requestId
├─ documentId
├─ documentRevision
├─ units
├─ preconditions[]
├─ operations[]
│  ├─ type
│  └─ type-specific validated fields
├─ postconditions[]
├─ risk
└─ planHash
```

`ExecutionPlan v1` 严格保留现有 MVP 规格的顶层字段，以及 `{ "type": "...", ...类型参数 }` 的扁平 operation 结构。capability 版本由已绑定的 manifest 表达，不插入 v1 operation。App 身份、Bridge/compiler 版本和 capability manifest 摘要属于执行与确认上下文，不作为 v1 顶层扩展。任何顶层变化，或引入 `id`、`arguments`、`dependencies` 等新 operation 结构的需求，都必须升级 `schemaVersion`，并提供明确的迁移或拒绝策略。

### 处理链路

```text
Server Agent
→ 根据 capability manifest 生成候选 ExecutionPlan
→ Local 解析、schema/策略校验、规范化并计算 planHash
→ App Bridge 做 App 级校验并生成只读预览
→ Web UI 展示并绑定用户确认
→ App Bridge 将 ExecutionPlan 编译为确定性的厂商 API 调用
→ 执行、验证、恢复并返回统一结果
```

### 设计规则

- DSL 是声明式、非图灵完备的数据结构，不允许任意脚本、循环、反射或厂商 API 函数名。
- 所有数值必须有限且显式携带单位；对象引用必须包含文档身份、revision 和稳定引用信息。
- 每个 operation 必须对应 capability registry 中已声明的操作。
- 顶层 `ExecutionPlan` schema 始终唯一。公共语义使用核心 capability；App 专属语义只能使用命名空间化、独立版本的 operation/capability argument schema，不能增加 App 专属顶层字段或改变公共语义。
- 未知版本、operation、字段、单位或越界参数一律拒绝。
- Server 输出始终视为不可信输入；Local 是通用 DSL 的权威校验与确认边界，Bridge 是当前文档和厂商 API 的最终校验边界。
- `planHash` 由 Local 基于规范化 DSL 重新计算，Server 提供的值不具权威性；用户确认后，任何参与哈希的内容都不可变。
- 用户确认同时绑定 `planHash`、文档 revision、App/Bridge/compiler 精确版本和 capability manifest 摘要；任一内容变化都使旧确认失效。
- Bridge 编译必须是确定性的；同一 DSL、App/Bridge/compiler 精确版本和 capability manifest 应产生等价执行序列。
- DSL 只描述“做什么”，Bridge 代码负责“如何调用当前 CAD API”。

## 6. 实施阶段

### P0：冻结边界与协议

交付：

- 建立 `Web`、`Local`、`Dsl`、`Contracts`、`SolidWorks Bridge` 和 `Server` 项目边界。
- 定义三类协议、版本协商、capability manifest 和统一错误模型。
- 定义 `ExecutionPlan v1`、JSON Schema、规范化规则、`planHash` 和核心 capability registry。
- 定义确认上下文：App/Bridge/compiler 版本、capability manifest 摘要及失效规则。
- 建立 C# / TypeScript 生成类型、纯校验器、Mock Compiler 与 golden DSL cases。
- 实现最小 Local Broker 与 IPC 骨架，作为 Web、Bridge 和 Server Mock 的唯一转发路径。
- 建立 Mock Web、Mock Bridge 和 Mock Server 契约测试。

Gate：

- Mock 端到端消息可以完整往返。
- Server、Local 和 Mock Bridge 对同一 fixture 得到相同的规范化结果与哈希；非法 DSL 在执行前全部拒绝。
- Local 不引用任何 CAD 厂商 SDK。
- UI 和 Server 无法绕过 Local 直接触达 CAD API。

### P1：SOLIDWORKS WebView UI 等价迁移

交付：

- 在现有 Task Pane 的 C# 插件壳中嵌入 WebView2。
- 迁移聊天记录、输入、快捷项、状态、确认和错误界面。
- 通过最小 Local Broker 的临时 compatibility adapter 将现有三条确定性命令映射为 `ExecutionPlan v1`，避免同时重写所有层；P2/P3 完成后删除该 adapter。

Gate：

- 现有交互与建模命令无回归。
- UI 使用统一 DSL 预览模型展示 operation、参数、风险和前后置条件，并确认 Local 计算的同一个 `planHash`。
- 首次加载、缩放、Resize、加载/卸载和输入法行为通过验证。
- WebView 崩溃或运行时缺失时可诊断，且不拖垮 SOLIDWORKS。

### P2：统一 C# Local Core

交付：

- 建立每个 Windows 用户单实例的 `Text2Cad.Local.exe`。
- 使用本地数据库实现会话、消息、设置、缓存和 schema migration。
- 持久化 DSL、校验结果、确认上下文、确认状态与执行结果，不保存可绕过校验的可执行对象。
- 实现 DSL 版本拒绝/升级策略；离线确定性命令同样经过完整 DSL 校验链路。
- 使用 `appId + appInstanceId + documentId` 隔离不同 App 和文档。
- 实现安全 Token 缓存、IPC、超时、取消、恢复和结构化日志。

Gate：

- 重启 CAD App 后会话能够恢复。
- 数据损坏可隔离或恢复，不阻塞 CAD App 启动。
- 从数据库恢复的 DSL 必须重新执行当前 schema、policy、capability 与 revision 校验，不能信任缓存的“已校验”状态。
- 不兼容 DSL 版本不得静默升级或执行。
- 只有规范化计划与完整确认上下文均未变化时才能恢复原确认，否则必须重新预览和确认。
- Local 退出、升级或断开时，CAD App 仍稳定可用。
- 离线时可浏览历史、设置并执行已登记的确定性本地命令；AI Agent 明确不可用。

### P3：抽取 SOLIDWORKS Bridge

交付：

- 将上下文采集、事件、STA/UI 调度、执行和验证从 UI 中移入 `SolidWorks Bridge`。
- 实现 SolidWorks DSL Compiler，将 `ExecutionPlan` operation 映射为确定性的 PIA/COM 调用。
- Bridge 握手上报 App、Bridge/compiler 精确版本与 capability manifest 摘要。
- 当前三条建模命令全部通过统一 DSL 与 Bridge 协议执行。
- 返回 operation 级执行、验证、错误与恢复结果。
- 只有 SolidWorks Bridge 引用 SOLIDWORKS PIA。

Gate：

- 现有 52 项 smoke checks 与 SOLIDWORKS 2026 真机回归通过。
- Web UI 和 Local 不包含 SOLIDWORKS 专属分支。
- 非法、过期或不支持的 DSL 在执行前被拒绝。
- capability manifest、Compiler、Executor 和真机 fixture 保持一致。
- 在相同 App/Bridge/compiler 精确版本、capability manifest 和相同干净初始 fixture 上，同一 golden DSL 重复执行产生等价的原生特征树。

### P4：独立 Server 与 AI Agent

交付：

- 用户注册、登录、Token 刷新、退出和基础授权。
- Agent 流式响应、模型 Provider、DSL 结构化输出和工具策略。
- 超时、取消、重试、限流、审计和基础可观测性。

Gate：

- 用户可完成登录并发起 Agent Run。
- Agent 能基于 Bridge capability 生成符合 DSL schema 的候选 `ExecutionPlan`。
- 断网或 Server 重启不会导致 CAD App 崩溃。
- Agent 先以 shadow mode 运行，只生成 DSL 而不执行。
- Server 可以预检，但所有候选 DSL 都必须由 Local 独立校验；未授权或不受支持的内容由 Local 拒绝，Bridge 再做 App 级最终校验。

### P5：端到端安全闭环

目标链路：

```text
Web UI
→ Local
→ Server Agent
→ 候选 ExecutionPlan DSL
→ Local 规范化、校验与 planHash
→ App Bridge 校验与预览
→ Web UI 确认
→ App Bridge 编译、执行与验证
→ Local 持久化结果
```

Gate：

- `planHash`、文档 revision、Bridge/compiler 版本或 capability manifest 摘要变化时拒绝旧计划。
- 未经用户确认的计划不能进入 Bridge 执行。
- 篡改、未知字段或绕过 capability registry 的 DSL 无法触达 CAD API。
- 每个 operation 都能关联到执行、验证与恢复结果。
- 失败后能够恢复或明确要求人工检查。
- 保存并重开后，结果仍是原生、可编辑的 CAD 特征。
- Server 和 Agent 始终没有直接执行 CAD API 的能力。

### P6：第二个 CAD App 验证

从 NX（UG）、Creo、AutoCAD 中只选择一个作为第二个 App，选择依据是用户价值、官方 API 成熟度和测试环境可得性。

交付：

- 新增该 App 的 C# Plugin 与 Bridge。
- 复用相同的 Web UI、Local、Server 和协议。
- 实现该 App 的 DSL Compiler，并增加 capability manifest 与真机 fixture。

Gate：

- 不复制或分叉 Web UI 与会话逻辑。
- 共享层不增加按 App 硬编码的业务分支。
- 相同核心 DSL 可由两个 Bridge 编译；差异只通过已声明的 capability 或 App 专属 operation argument schema 表达。
- 新增工作主要集中在插件生命周期、API Bridge 和 App 专属测试。

通过第二个 App 的 Gate 后，再按优先级逐个接入其余 CAD App。

## 7. 推荐目录

```text
schemas/
  dsl/v1/                      ExecutionPlan JSON Schema
src/
  Text2Cad.Web/                 共享 WebView 前端
  Text2Cad.Dsl/                 CAD DSL schema、生成类型、校验与编译器接口
  Text2Cad.Contracts/           版本化协议与 DTO
  Text2Cad.Local/               C# 本地数据与通用业务逻辑
  Bridges/
    Text2Cad.SolidWorks/        SOLIDWORKS Plugin + Bridge
    Text2Cad.NX/                NX Plugin + Bridge
    Text2Cad.Creo/              Creo Plugin + Bridge
    Text2Cad.AutoCAD/           AutoCAD Plugin + Bridge
server/
  Text2Cad.Server/              用户系统与 AI Agent
tests/
  Dsl/
  Contracts/
  Local/
  Bridges/
  EndToEnd/
```

各 CAD 支持的 .NET 运行时可能不同，因此共享的是协议、语义和前端产物，不强求所有 Bridge 引用同一个二进制程序集。

## 8. 当前非目标

- 同时开发多个 CAD Bridge。
- 完整覆盖任一 CAD 的全部 API。
- Server 直接访问 CAD、本地文件或用户完整模型。
- Agent 生成并执行 C#、VBA、宏或原始厂商 API 调用。
- 用一个“万能 operation”或不受 schema 约束的参数包绕过 DSL capability。
- 会话云同步、团队协作、企业权限和微服务拆分。

这些能力只在 SOLIDWORKS 闭环与第二个 App 验证通过后再单独规划。
