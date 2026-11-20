# Snoop Next — 可直接转成 GitHub Issues / PR 的实施清单 v1.0

设计基准：HTML v1.1 + `02-ui-interaction-mapping.md` + `03-resx-localization.md` + `04-mcp-host-and-ipc-contract.md`。  
建议先在 Fork 的 `develop` 上以小 PR 实施，不直接改默认分支 `develop`（当前不存在 `main`）；保留 `upstream/develop` 的追踪。**以下是产品实施任务，文档已纳入 PR，但生产功能尚未实现。**

## 0. 开发纪律（提交到 docs/CONTRIBUTING.md）

每个 PR 模板包含：

- 对应原型验收 ID（例如 `A-03`, `M-09`）、设计截图（Light+Dark）、涉及窗口。
- 最低 TFM 编译、目标运行时 smoke、x86/x64（关联到 Launcher/Probe 时）、静态分析与回归结果。
- 属性编辑 / MCP 的安全威胁分析、取消/断开路径、失败后状态回收。
- 记录与 upstream 原文件的冲突区域；修改大型上游 XAML 必须解释不可避免性。
- UI 新文案须加入 `Strings.resx` 与 `Strings.zh-CN.resx` 并通过资源 Key 检查。
- 不仅通过编译，还需证实行为与 HTML 对应；原型差异有变更记录和新的验收预期。

## 1. P0 — Fork 基线与目标运行时门禁

| Issue | 范围 | 最小产出 | 验收/证据 |
|---|---|---|---|
| `SN-001` | Fork 与 upstream 关联 | Branch strategy、LICENSE、ADR、VERSIONING | `develop` 受保护；可 fetch/merge 上游 |
| `SN-002` | Snoop 基线环境 | 原版 compile + `dotnet --info` + package lock | net462、net10 WPF Harness 成功附加 |
| `SN-003` | 多目标与 PolyfillLib 实验 | 中央包版本、TFM 条件构建、依赖闭包报告 | net462 加载、net6/8/10/11 smoke；没有 BCL 缺失异常 |
| `SN-004` | 目标进程 Harness | net462 x86/x64、net6/8/10/11 WPF 进程 | 进程退出/重附加/错误案例可重复运行 |
| `SN-005` | 确立 UI 视觉基线 | 将 HTML v1.1 与 Acceptance 纳入 `docs/next/prototype` | HTML SHA256、关键状态截图、差异审批流程 |

**P0 exit**：原版有可回归的附加能力与测试对象；未来任何架构拆分不能破坏它。

## 2. P1 — RESX 与 UI 基础主题

| Issue | 范围 | 最小产出 | 原型 ID |
|---|---|---|---|
| `SN-010` | Localization Manager + ResourceManager | neutral/zh-CN、缺失回退、运行时通知 | S-01, S-02 |
| `SN-011` | `LocExtension`、Settings | Toolbar/Menu/Tooltip 更新、保存语言 | S-01, S-02 |
| `SN-012` | 资源隔离验证 | 目标 Culture/Resources 不变 | I18N-02 |
| `SN-013` | Theme Tokens 与控件模板 | Light/Dark、Focus 状态、Density | S-01, D-05 |

**P1 exit**：两个语言及主题切换不影响被检查应用，最低 TFM 可正常加载。示例项目仅用于加速，不视为已通过真实构建。

## 3. P2 — WPF 工作台与属性体验

| Issue | 范围 | 最小产出 | 原型 ID |
|---|---|---|---|
| `SN-020` | Shell 与三栏 Dock | 导航 Rail、菜单/命令/状态栏、Splitter | D-04, D-05 |
| `SN-021` | Process Discovery + Attach | 按名称/PID/架构筛选、错误重试 | P-01…P-04 |
| `SN-022` | TargetSession 与统一 Selection | session GUID、进程退出、同 PID 重启 | P-05…P-07, T-04 |
| `SN-023` | Visual/Logical/Automation Tree | 按需加载、搜索、F6 拾取、定位 | T-01…T-05 |
| `SN-024` | Preview 与 DPI overlay | 截图、选中矩形、树预览同步 | T-04, D-07 |
| `SN-025` | Property Inspector + Origin | 属性搜索、来源识别、Binding | A-01, A-02, A-07, A-08 |
| `SN-026` | Edit + Validation + Undo | 修改、只读、撤销/重做、取消 | A-03…A-06 |
| `SN-027` | Diagnostic/Event/Command | 诊断定位、日志、Ctrl+K、导航 | D-01…D-03, D-06 |

**P2 exit**：从进程附加到树、预览、属性修改、诊断定位的完整 E2E；Light/Dark+中英截图与 HTML 行为对照。任何未实现的模拟菜单按钮必须标记为“未实现”，不能静默无效。

## 4. P3 — 可靠的跨进程 Inspection Bridge

| Issue | 范围 | 最小产出 | 原型 ID |
|---|---|---|---|
| `SN-030` | Abstractions + DTO + schema | JSON 版本、错误码、契约测试 | P-07, M-09 |
| `SN-031` | Dispatcher Probe | 有界查询、取消、超时、目标退出 | T-01, A-07 |
| `SN-032` | Named Pipes + 身份认证 | 同用户 ACL、通信认证、版本协商 | M-02, M-12 |
| `SN-033` | Handles 与分页 | 每次 attach 新 GUID、WeakReference、树分页 | P-07, M-09 |
| `SN-034` | Host/Probe 测量 | 压力/内存/操作超时、IPC 数据限额 | D-01, M-09 |

**P3 exit**：独立 net10 Host 可通过 IPC 向 net462 和 net10 Harness 探针读取视觉树；混入其它系统会话或已断连进程时正确拒绝。

## 5. P4 — MCP 服务、授权与受控写入

| Issue | 范围 | 最小产出 | 原型 ID |
|---|---|---|---|
| `SN-040` | MCP Host lifecycle | UI 启停、仅回环、Origin/认证检查 | M-01, M-10 |
| `SN-041` | Discovery/read tools | list_processes/get_visual_tree/inspect_element/get_properties | M-02 |
| `SN-042` | Authorization + Consent | 只读、一次性、会话、附加授权、弹窗并发 | M-03…M-08, M-11, M-12 |
| `SN-043` | Write Tools + Audit | `set_property`, `invoke_element` 最小白名单 | M-04…M-07 |
| `SN-044` | TOCTOU、Idempotency、Handle invalidation | 目标退出、同 PID 重附加、重复 requestId | M-09, M-11 |
| `SN-045` | 配置复制与审计/授权管理 | 客户端列表、撤销、敏感信息脱敏 | M-01, M-07 |

**P4 exit**：真实 MCP 客户端（Codex CLI 等）调用只读工具可用；写入和附加必须通过 UI 授权；所有操作都有结构化结果与审计；无“任意 CLR 方法执行”功能。

## 6. P5 — 发布、可维护与上游同步

| Issue | 范围 | 最小产出 |
|---|---|---|
| `SN-050` | 框架/架构矩阵 | x86/x64 & net462, net6, net8, net10, net11，视可用性扩展 net7/9/arm64 |
| `SN-051` | UI 回归 | 1280×720/1440×900，缩放 100–200%，Light/Dark、中英 |
| `SN-052` | 包装/分发 | Host 按运行时架构打包，目标程序集与依赖闭包验证 |
| `SN-053` | upstream 合并演练 | 专门集成分支、冲突报告、全量 smoke |
| `SN-054` | 安全检查 | Threat model、依赖漏洞、IPC/Origin/认证/审计脱敏 |

## 7. 推荐 PR 顺序与合并门禁

```text
PR-001: SN-001..005  基础兼容 / baseline
PR-002: SN-010..012  RESX/语言绑定
PR-003: SN-013,020    主题/工作台壳
PR-004: SN-021..024  附加/树/预览
PR-005: SN-025..027  属性/诊断/编辑
PR-006: SN-030..034  Bridge/Probe
PR-007: SN-040..041  仅只读 MCP
PR-008: SN-042..045  授权与受控写入
PR-009: SN-050..054  矩阵、发布、安全、上游同步
```

并行可以发生在不共享同一原始 XAML 文件的任务之间；例如国际化资源盘点与 Bridge Schema、Harness 测试可并行。避免多个 Codex Worktree 同时改 `SnoopUI.xaml`，尽量先抽 `Next/UI` View。

## 8. 测试层次

- `Level 0`：XML/JSON schema/RESX Key 检查、源生成/Analyzer、最低 TFM 编译。
- `Level 1`：纯 VM 单元测试、依赖属性来源单元测试、授权状态机模型测试。
- `Level 2`：Windows WPF Harness 实测 UI、焦点、撤销、DPI、进程退出。
- `Level 3`：真实 IPC 与 MCP 客户端端到端测试；只读/写入/授权竞态与拒绝。
- `Level 4`：资源使用、长时间事件追踪、上游补丁合并演练；低资源机器上测试。

**不要**每一处 CSS/RESX 小改动都运行全套 Launcher × Runtime 的重测试；以按变更影响选择快速 smoke，PR 合并和发布时跑完整矩阵。

## 9. 任务可追溯性

`traceability/acceptance-to-wpf.csv` 由 HTML v1.1 Acceptance 矩阵提取，不创造新的“原型已实现”结论。每条对应建议模块与测试场景，后续为每个 Issue 添加 PR / Commit / 结果。矩阵的 `status` 初始均为 `not_started`，不要因 HTML 模拟已通过就把 WPF 状态标成完成。