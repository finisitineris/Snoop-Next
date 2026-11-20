# Snoop Next · HTML 交互原型验收清单

**原型版本**：v1.1 / 2026-11-20  
**定位**：独立 Snoop WPF Fork 的视觉与交互基准，不是 WPF 实现，也不是真实 MCP Server。  
**设计基准**：Visual Studio 专业工作台布局 × Fluent 视觉 × DevTools 属性检查交互。  
**运行方式**：现代 Edge / Chrome 双击 `Snoop_Next_Interactive_Prototype_v1.1.html` 离线打开；不需要 HTTP 服务、CDN 或 npm。

## 1. 视觉与布局约束

| 设计项 | HTML 原型 | 后续 WPF 验收 |
|---|---|---|
| 全局容器 | 上方标题 + 菜单 + 命令栏；左侧功能 Rail；状态栏 | 模块顺序、导航入口、目标状态一致 |
| 核心工作台 | 左树 + 中心预览/底部诊断 + 右侧属性，分隔线可拖动 | 多栏 DockPanel/Grid、键盘可达、收放与还原一致 |
| Fluent 视觉 | 浅色/深色、中性表面、低饱和蓝、6–12px 圆角、细分割线 | 色彩、阴影、边距、焦点与警告状态使用 Design Tokens |
| DevTools 属性交互 | 值/来源双列、排序、来源路径、双击/点击编辑、回滚 | 原因/来源可追踪，验证失败不会污染 UI 状态 |
| 尺寸 | 基准 1440×900；最低目标 1280×720 | 避免工具栏重叠，长英文文案不裁掉关键操作 |
| 密度 | Comfortable / Compact | 元素树、诊断与属性行距随密度变化 |

## 2. 手工验收矩阵

| ID | 操作 | 预期结果 | WPF 落地组件/模块 |
|---|---|---|---|
| P-01 | 初次打开 | 进程选择器列出 SampleApp、LegacyOrder、AdminConsole；清楚标记模拟 | ProcessPicker |
| P-02 | 搜索 `41920`，切换运行时筛选/按 PID 排序 | 进程列表同步筛选；空结果可恢复 | ProcessFilter |
| P-03 | 附加 `SampleApp.exe` | 短暂显示附加中，进入三栏工作台并默认选中 SaveButton | AttachCoordinator |
| P-04 | 附加 `AdminConsole.exe` | 权限受限、错误对话框、重试入口；不会保留旧目标 | AttachError |
| P-05 | 工作台点“断开连接” | 返回进程选择器，旧对象和属性修改失效；主题/语言仍保留 | TargetLifecycle |
| P-06 | 状态栏模拟目标退出 | 目标退出反馈，MCP 服务可继续运行，但目标授权失效 | TargetExited |
| P-07 | 状态栏模拟同 PID 重启 | 新进程实例标识，旧历史与授权不可复用 | ProcessInstanceGuard |
| T-01 | Visual / Logical / Automation 模式切换 | 元素树变化，选择状态稳定 | TreeMode |
| T-02 | 展开/折叠 RootGrid | 子节点相应显示/隐藏 | VirtualTree |
| T-03 | 搜索 `NameInput`、输入无匹配字符串、清空 | 筛选命中/空状态/恢复，不丢失选中 | TreeSearch |
| T-04 | 点击 SaveButton 树节点或中心按钮 | 面包屑、预览高亮、右侧属性与底部状态栏同步 | SelectionService |
| T-05 | 按 F6；点击预览节点 | 拾取状态有反馈，预览节点可选且可键盘触达 | ElementPicker |
| A-01 | 右侧搜索 `Content` | 仅列出匹配属性，WPF API 名称原样保留 | PropertySearch |
| A-02 | 筛选 Binding/Local、仅已修改、按来源排序 | 来源/修改列表正确 | PropertyFilters |
| A-03 | 点击属性值 `SaveButton.Content`，输入 `完成`，提交 | 列表与中心按钮同步更新，出现已修改标记 | PropertyEditor |
| A-04 | 撤销、重做；切换节点再返回 | 恢复保存/完成，修改与历史仍一致 | UndoManager |
| A-05 | 编辑 Width 为 `-1` | 规则验证错误；不记录无效修改，不影响预览 | Validation |
| A-06 | 试图修改 ActualWidth | 显示只读错误，值不变 | ReadOnlyMetadata |
| A-07 | 点击 `Command` 的 Binding 来源 | 显示绑定 Path、Source、解析状态 | ValueOrigin |
| A-08 | 绑定/触发器/事件标签切换 | 展示匹配模拟数据或明确空状态 | PropertyTabs |
| D-01 | 底部诊断切换 Events / Output | 内容切换；搜索与清空可执行 | DiagnosticsDock |
| D-02 | 展开 Binding 错误、点击定位 | 跳到 NameInput，显示 ToolTip 来源 | DiagnosticNavigation |
| D-03 | 打开侧边诊断中心和事件页 | 可浏览问题/事件；导航回工作台 | DiagnosticsPage |
| D-04 | 折叠/恢复树、右侧属性、底部诊断 | 状态与面板内容保持；无核心控件丢失 | DockLayout |
| D-05 | 拖动横竖分隔线、在设置中换布局、重置 | 宽度/高度受边界限制，布局可恢复 | LayoutPersistence |
| D-06 | Ctrl+K 搜索 MCP 或 Settings；Esc 退出 | 命令面板可操作，键盘交互清晰 | CommandPalette |
| D-07 | 截图页生成模拟截图 | 显示目标控件与模拟完成反馈；不向磁盘写真实截图 | CapturePreview |
| M-01 | 进入 MCP 页，不启动服务 | 显示停止与 127.0.0.1 示例端点；无真实端口 | McpDashboard |
| M-02 | 启动，模拟列出进程/读取树/检查节点 | 状态变运行中，审计记录允许的读取工具 | McpLifecycle/ReadTools |
| M-03 | 默认只读模式下请求 set_property | 自动拒绝并写审计；属性未变化 | PermissionPolicy |
| M-04 | 切换“允许请求写入”，请求属性写入后拒绝 | 目标、客户端、参数清晰；拒绝后值不变，审计记录拒绝 | PermissionPrompt |
| M-05 | 再请求，选“仅本次允许” | 成功修改 SaveButton.Content，产生可撤销的历史和审计；下一次仍提示 | OneShotGrant |
| M-06 | 选“本次会话允许”，再由同一客户端写同一工具 | 范围内不再提示，所有调用仍审计 | SessionGrant |
| M-07 | 改变客户端、进程实例或工具、点击撤销 | 超出授权范围后不能静默写入；授权失效 | ScopedGrant |
| M-08 | AI 请求附加 LegacyOrder | 必须在 UI 内确认；拒绝时不切目标，批准后切目标并记审计 | AttachConsent |
| M-09 | 写入授权弹窗出现时断开目标、停止服务 | 待决写入取消或拒绝，旧引用无效，不修改新目标 | RaceGuard |
| M-10 | 触发“模拟启动失败”，然后重试启动 | 可见错误、重试与启动/停止过渡状态 | McpError |
| M-11 | 授权对话框未处理时重复发起写入/附加请求 | 新请求被拒绝并审计；原始待确认请求不能被覆盖 | PendingConsentConcurrency |
| M-12 | 未连接或未知的 MCP 客户端尝试读取、附加或修改 | 统一拒绝、写入审计，不能出现授权弹窗或属性变化 | ClientConnectionGuard |
| S-01 | 设置切换中/英、浅/深/系统、舒适/紧凑 | 界面变化且当前元素、属性修改、MCP 状态不丢 | I18n/Theme/Density |
| S-02 | 切换英文，浏览属性表与树 | `SaveButton`、`ActualWidth`、`Binding` 等 API 名称不翻译；按钮可见 | Resx/Localization |
| S-03 | 点击重置演示数据并确认 | 恢复模拟控件值及历史，保留用户偏好 | DemoReset |

## 3. 安全与数据边界

- 本文件及 HTML 中所有 PID、对象、URL、客户端和审计记录均为示例。**HTML 不监听端口、不枚举本机真实进程、不注入 WPF、不发出网络请求**。
- MCP UI 演示默认只读：列出进程可以读取；附加新目标始终要用户确认；写入需要选择一次或限会话授权。
- 会话授权三元范围：客户端身份 + 目标进程实例（PID + 启动标识）+ 允许工具集合。断开、重启、停止、撤销时失效。
- 生产实现建议独立 .NET 10/11 MCP sidecar、loopback HTTP、Named Pipes 到检查组件。原型不证明实际传输、安全握手或跨框架兼容性。

## 4. 实现对照及回归验证

1. **功能基准**：以本验收清单的可观察行为、模态流程、错误文案语义、可访问性为准，不以 HTML DOM 作为 WPF 架构要求。
2. **视觉基准**：以高保真原型与已确认的混合草图为准，WPF 使用 Design Tokens；重大界面差异留设计变更记录。
3. **国际化**：正式实现 RESX + 自定义轻量动态 WPF 绑定层。保留 `zh-CN`、`en-US`、Key、运行时切换能力。
4. **框架兼容**：正式 Fork 目标 .NET Framework 4.6.2 ～ .NET 11；现代 C# 语言能力可使用，但不引入无法 polyfill 的运行时要求；`PolyfillLib` 作为编译/兼容依赖审查。
5. **测试命令**：`node --test prototype/tests/*.test.cjs`；`node prototype/scripts/build.mjs`；`node --check prototype/src/js/model.js` 等。
6. **浏览器端 E2E**：在具备本地 HTML 访问权限的 Windows Runner 中安装 Playwright 后，运行 `RUN_BROWSER_E2E=1 node --test prototype/tests/e2e.spec.cjs`（Windows 环境可设置 `CHROME_PATH` 指向 Edge/Chrome）。

### 本次交付的验证边界（2026-11-20 更新）

- **44/44 Node.js 测试通过**：包含进程实例隔离、权限竞态、属性修改、国际化、DOM 渲染与离线打包。
- **5/5 Playwright Chromium 流程通过**：通过 `page.set_content` 将完整单文件 HTML 加载到内存页面中测试，真实点击覆盖进程附加、属性编辑/撤销重做、语言/主题/密度切换、MCP 只读/单次授权、批准后退出、异常与快捷键。另单独验证了树搜索和属性搜索在 1280×720 下的浏览器行为。
- 浏览器代码未访问网络，测试中没有启动 MCP HTTP 服务；该方法验证的是 **HTML/CSS/JS 的真实浏览器交互**，而非直接打开 `file://` 文件。
- 受管图形浏览器仍限制直接 `file://` 访问，因此 **未在此受管浏览器中验证双击 HTML 打开的体验**。建议仍在用户的 Windows Edge/Chrome 中执行一次最终离线打开复验。
- 为防止输入中重绘引发焦点/DOM 错误，输入值在模型中同步更新，搜索过滤在下一动画帧重新渲染；该问题由实际 Chromium 测试发现并修正。

### v1.1 修订记录

1. MCP 请求加入客户端连接状态校验：离线/未知客户端不得读取、附加或修改。
2. 为未处理的写入授权、附加授权和已批准待执行请求增加互斥防护；重复请求拒绝并记审计，不可覆盖前一个请求。
3. 修复属性输入事件期间全窗口重绘引发的 DOM/焦点异常。
4. 修复浏览器回归脚本在目标退出后仍处于 MCP 页面、误查找进程列表的问题。
5. 保留所有示例进程、PID、连接端点和 MCP 行为均为模拟这一边界。
