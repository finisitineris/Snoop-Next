# Snoop Next — MCP Host / IPC / 权限契约 v1.0

**重要**：这是将 HTML v1.1 的可观察行为提升为正式工程契约的设计，不表示本次交付已经启动真实 MCP Server。

## 1. Transport 与部署决定

- **MCP Host 独立进程**，默认使用 .NET 10 + `ModelContextProtocol.AspNetCore`，允许有独立 .NET 11 发行版；UI 通过本机受认证控制通道启动/停止 Host。
- 默认 `Streamable HTTP`，固定路由 `/mcp`，绑定 `127.0.0.1` 的可配置端口。**不**绑定 `0.0.0.0`；不同用户会话不得互通。
- 实现时锁定一份 MCP 协议版本与 C# SDK 版本，并对官方 2026-07-28 规范建立兼容测试。`Mcp-Session-Id` 仅是协议会话字段，**不能当作已验证客户端身份**。
- 所有 HTTP 请求必须校验 `Origin`（若发送且不在允许范围 → HTTP 403），使用独立认证机制（如首次配对的本机受限令牌或经过验证的客户端身份/本机命名管道凭据），限制请求体/并发/速率。不要将本地回环绑定误认为“无需身份验证”。
- 可在未来提供 `stdio` 启动模式以供某些 MCP 客户端自动拉起，此模式仍经过同一权限引擎；不能通过 `stdout` 输出普通日志。
- Host 不加载目标进程的 `Visual`、`DependencyObject` 或 Snoop UI 控件程序集；IPC 用基本 JSON DTO。

官方参考：
- <https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/docs/specification/2026-07-28/basic/transports/streamable-http.mdx>
- <https://csharp.sdk.modelcontextprotocol.io/concepts/getting-started.html>
- <https://csharp.sdk.modelcontextprotocol.io/v1/concepts/transports/transports.html>

## 2. 会话、身份与句柄模型

```text
AuthenticatedClientPrincipal  # 由 Host 传输认证产生，不信任 tool 参数中的任意 clientId
  clientId: UUID
  displayName: string
  userIdentity: SID / trusted local principal
  connected: bool

TargetSession
  attachmentId: UUID           # 每次附加必新建，防 PID 复用
  pid: int
  processStartUtc: ISO-8601
  architecture: x86/x64/arm64
  runtime: string
  capabilities: string[]
  state: attached/exited/detached

ElementHandle
  opaque ID (随机、不可猜测、只对特定 attachmentId 有效)
  expires with TargetSession / weak target reference
```

- `clientId` 由认证层建立，不通过 tool 入参直接决定。
- 附加新目标必须显示 `进程名 + PID + 启动时间 + 进程路径/签名（可获取时） + 请求客户端` 的明确同意窗口。
- `ElementHandle` 在跨目标、断连、目标进程退出、重新附加、无效对象 GC 时失效。返回 `STALE_HANDLE` 而非凭显示名称自动匹配另一对象。
- 日志中的 token、敏感业务属性值、窗口文字和完整截图默认**不持久化**；按明确策略脱敏、保留执行结果、工具名称和目标实例指纹。

## 3. MCP v1 工具目录（工具名稳定、全部 snake_case）

| Tool | 权限 | 关键参数 | 结果 | 行为 |
|---|---|---|---|---|
| `list_processes` | Discovery | `query?`, `runtime?`, `pageSize?` | 进程摘要及分页 | 不自动附加，按 OS 权限与进程可见性过滤 |
| `request_attach` | Approval | `processId`, `processStartUtc`, `requestId` | 已审批且附加的 targetSession，或 DENIED | 无论是否已被 UI 附加，AI 新目标请求均需用户确认 |
| `get_visual_tree` | Read | `attachmentId`, `root?`, `depth`, `pageSize`, `cursor?` | 部分节点 + opaque handles | 有界返回，避免完整树倾倒 |
| `get_logical_tree` | Read | 同上 | 逻辑树节点列表 | 与 WPF 逻辑树一致 |
| `get_automation_tree` | Read | 同上 | 自动化模式元素摘要 | 受 UIA 能力与安全限制 |
| `inspect_element` | Read | `attachmentId`, `elementHandle` | 类型、名称、路径、bounds、capabilities | 不返回任意对象图/秘密 |
| `get_properties` | Read | `attachmentId`, `elementHandle`, `filter?`, `pageSize?` | 属性值、值源、可写性、Binding | 属性字符串输出大小有上限 |
| `get_binding_diagnostics` | Read | `attachmentId`, `elementHandle?`, `limit?` | Binding 错误和来源 | 同步到 UI 诊断中心 |
| `capture_element` | ReadSensitive | `attachmentId`, `elementHandle?`, `maxWidth?` | image metadata + image content | 截图可能含敏感内容；单独开关与频率限制 |
| `set_property` | Write | `attachmentId`, `elementHandle`, `property`, `value`, `mode`, `requestId` | 改前/后值、来源、undoToken? | 每次执行前校验授权与目标实例，避免破坏绑定 |
| `invoke_element` | Write | `attachmentId`, `elementHandle`, `action`, `requestId` | 操作结果 | UI Automation 允许动作白名单；方法调用等高风险另开能力 |
| `detach_target` | WriteControl | `attachmentId`, `requestId` | 断开状态 | 用户可撤销，AI 控制操作需单独许可 |

工具 JSON Schema 片段见 `contracts/mcp-tool-catalog.json`。`screenshot`、`get_events`、`get_triggers`、`get_resources` 视 P4 工程能力扩展，而非一开始承诺无限接口。

## 4. 统一结果与错误模型

MCP Tool Result 使用 SDK 与对应规范要求的外层消息；工具的 `structuredContent`（如客户端支持）统一为：

```json
{
  "schemaVersion": "1.0",
  "requestId": "generated-or-client-supplied-id",
  "attachmentId": "ATTACHMENT-GUID",
  "ok": true,
  "data": { "items": [], "nextCursor": null },
  "error": null
}
```

失败示例（错误代码**不可翻译**）：

```json
{
  "schemaVersion": "1.0",
  "requestId": "b36a2e",
  "attachmentId": "ATTACHMENT-GUID",
  "ok": false,
  "data": null,
  "error": {
    "code": "STALE_HANDLE",
    "message": "Element handle belongs to a detached target",
    "retryable": false
  }
}
```

错误码：`UNAUTHENTICATED`, `CLIENT_DISCONNECTED`, `PERMISSION_REQUIRED`, `PERMISSION_DENIED`, `CONSENT_BUSY`, `TARGET_NOT_ATTACHED`, `TARGET_EXITED`, `STALE_HANDLE`, `NOT_SUPPORTED`, `READ_ONLY_PROPERTY`, `INVALID_VALUE`, `TIMEOUT`, `CANCELLED`, `RATE_LIMITED`, `INSPECTOR_ERROR`。

所有 ToolError 要映射到 MCP 规定的 tool-level 错误或协议层错误，不把业务错误伪装 HTTP 500；客户端没有结构化支持时给简短文本摘要，避免泄漏业务机密。

## 5. 原生 IPC 约束（Named Pipes）

- Named Pipes 只在本机，访问 ACL 限定同一用户 SID + UI/probe 授权用户，Host 与 Probe 需双向证明身份/连接方，防同机冒用。
- IPC Envelope 见 `contracts/ipc-envelope.schema.json`：`protocolVersion`、`requestId`、`attachmentId`、`operation`、`deadlineUtc`、`payload`；操作超时与拒绝必须释放 Dispatcher work item。
- 单个逻辑请求不能无限长，树分页限制 250 节点/页、深度最大 6（**初始建议，可配置并实测**），输出总字节不超过协议上限（建议 2MiB 结构化文本）。截图另走有界的分块/二进制传输，避免超大 base64 消息。
- IPC 线程不能直接触碰 WPF `DependencyObject`，须 marshal 到对应目标 `Dispatcher`。目标死锁/挂起时返回超时；禁止无限同步阻塞。
- 版本协商：`protocolVersion=1.0`；兼容扩展使用 capabilities；breaking changes 升大版本。旧 Probe 明确返回不支持，不猜测数据字段。
- 如果 UI 在目标进程中，不能把高权限审批绕过 Host 直接进入 Probe；所有来自外部 AI 的写入要带上经 Host 校验的最小能力授权上下文，Probe 也检查 attachmentId/operation。

## 6. 审批状态机

```text
Unconnected/Unauthenticated
    → Reject (no prompt)
Connected & tool read
    → authorized & target valid ? Execute : Reject
Connected & tool write
    → mode readonly ? Reject : PermissionPolicy
PermissionPolicy
    → session grant matches (client + attachment + tool + constraints) ? RevalidateAndExecute
    → no grant: PendingConsent (single pending per target/client policy)
PendingConsent
    → Deny / AllowOnce / AllowSession / Dismiss / Disconnect / HostStop / Timeout
AllowOnce
    → request parameter hash verified → consume once → RevalidateAndExecute
AllowSession
    → scoped grant with expiry and UI revoke → RevalidateAndExecute
RevalidateAndExecute
    → re-check client, attachmentId, target process start, tool, permission, requestId
    → Probe executes once and reports result → Audit
```

- **单次授权绑定完整请求**：客户端、目标 attachment、具体对象、属性/动作、参数摘要/Hash。用户点击 AllowOnce 后输入改变 → 原授权不得重用于新参数。
- **会话授权边界**：`AuthenticatedClientId + AttachmentId + AllowedToolSet`，加入有效期、可选参数限制和明确的撤销入口。更换目标/重新连接/Host 停止/客户端断连立即撤销；默认不跨应用重用。
- **并发**：同一目标的一项待确认授权不能被覆盖；重复请求返回 `CONSENT_BUSY`；审批 Modal 必须给出请求 ID。
- **Replay**：写操作使用 `requestId` + request fingerprint + attachmentId 幂等缓存；同一请求不得执行两次，重试返回已有结果或可辨别未完成状态。
- **TOCTOU**：提交审批之后到实际写入期间可能退出，须执行前重新确认目标存活/句柄有效；若失败不写新目标、不复用同 PID。
- **截图**：默认只读但仍可泄漏隐私，应属于 `ReadSensitive` 并有单独允许开关；无需破坏已约定“默认只读”，但安全设置可更保守。

## 7. 典型 JSON 调用契约

**get_visual_tree**：

```json
{"attachmentId":"ee0309aa-80d1-4ad1-a1d8-658e4219ef21","root":null,"depth":2,"pageSize":100}
```

返回 `items[{handle,parentHandle,type,name,childCount,hasMoreChildren}]`, `nextCursor`, `capturedUtc`, `partial`。禁止返回未经截断的大量属性字符串。

**set_property**：

```json
{"attachmentId":"ee0309aa-80d1-4ad1-a1d8-658e4219ef21","elementHandle":"opaque-handle","property":"Content","value":{"type":"string","value":"完成"},"mode":"preserve_binding","requestId":"9ff0f030-2fd8-4c04-878d-eb4019d0d405"}
```

返回 `before`, `after`, `valueSource`, `bindingPreserved`, `canUndo`, `undoToken?`。只有经明确确认 `overwrite_local` 时才可覆盖绑定/清除来源，且应对无损回滚单独标记。

## 8. 生产级测试要求

| ID | 场景 | 预期 |
|---|---|---|
| SEC-01 | 外部网页发起跨 Origin 请求 | 403 或策略拒绝 |
| SEC-02 | 未认证/离线客户端请求任何工具 | 拒绝 + 审计，不显示批准弹窗 |
| SEC-03 | 默认只读 `set_property` | 被拒、原值不变 |
| SEC-04 | 同一个待审批请求期间重复写/请求附加 | 返回 CONSENT_BUSY，不能覆盖已有弹窗 |
| SEC-05 | 仅本次授权修改后，再重复相同写入请求 | 无新许可的独立操作再次需要授权；相同 requestId 幂等返回，不重复执行 |
| SEC-06 | 会话授权更换 clientId、attachmentId 或 tool | 拒绝；新目标不得复用旧授权 |
| SEC-07 | 目标退出/断开/同 PID 重新附加 | STALE_HANDLE；旧许可失效 |
| SEC-08 | 允许后、执行前目标断开 | 操作取消、不修改新目标、审计有原因 |
| SEC-09 | 无限树/截图大图/连续调用 | 强制限流、分页或大小上限，不阻塞 WPF UI |
| SEC-10 | IPC 对端假冒/不同用户 SID | 连接被拒；HTTP token 不写日志 |
| SEC-11 | 用户从 UI 停止 MCP Host | 端口关闭、pending 请求拒绝、授权全撤销 |
| SEC-12 | 用户在只读模式触发截图 | 若禁用敏感读取，单独拒绝并提示 |

## 9. 任务划分（依赖）

1. `Snoop.Bridge.Contracts`：模型 + schema + 序列化 round-trip + 版本。
2. `InspectionFacade`：复用现有树、属性、诊断；有界 Dispatcher 操作。
3. `Probe`：Named Pipes 与安全句柄表 + 断连取消。
4. `McpHost`：身份认证 + 读工具 + 启停 + 审计。
5. `PermissionService`：待批准请求、一次性/会话授权 + 幂等 + 审批 UI。
6. `Write/Automation`：受控修改与交互，最后开放。

不要在第一阶段实现所谓“AI 可以执行任意 C# 方法”或“任意代码注入”，这不是所批准的工具范围。