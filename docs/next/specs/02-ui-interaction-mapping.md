# Snoop Next — HTML v1.1 → WPF 交互映射规范 v1.0

核心原则：**以用户可观察行为为准，不将 HTML DOM/JS 当作 WPF 代码模板。** 首版 WPF 主窗口保留旧版 Snoop 的能力，并扩展 AI/MCP 控制中心。

## 1. 设计基准与尺寸

- 默认参考视口：1440×900；最低目标 1280×720；100%、125%、150%、200% DPI 都应做视觉回归。
- Frame：TitleBar（标题/连接/MCP 状态）、MenuBar、CommandBar、NavigationRail、InspectorWorkspace、StatusBar。
- Workspace：左侧树、中间预览+底部诊断、右侧属性；左/右列宽及底部高度可调。
- 默认逻辑尺寸（DIP，不是物理像素）：Tree=282，Property=354，Bottom=210；范围 Tree=165–500、Property=240–590、Bottom=110–440。
- 布局：`default=[282,354,210]`、`focus=[210,285,160]`、`inspect=[270,460,240]`；持久化大小与折叠状态；主屏 DPI 改变后重新 clamp。
- 原型 v1.1 保留其历史配色。当前 WPF 窗口使用第 7 节的 Windows 11 Fluent 色板，资源限定在 Snoop 窗口内。
- 允许按钮文字随本地化变长，显示 Ellipsis/Tooltip；重要的「附加 / 断开 / 授权」不能仅用图标且无 ToolTip。

## 2. 控件与 ViewModel 映射

| HTML 区域/行为 | WPF 技术选项 | VM/Service | 关键验收 |
|---|---|---|---|
| WindowBar、主菜单 | `WindowChrome`（可选）、`Menu`, `CommandBinding` | `ShellViewModel`, `CommandService` | 连接/MCP 状态始终可见 |
| 左 NavigationRail | `RadioButton`/`ListBox`+ `ContentControl` | `NavigationState` | 键盘可切换、当前项指示 |
| ProcessPicker | `ListView` + `CollectionViewSource` | `ProcessDiscoveryViewModel` | P-01..P-07 |
| Attach 对话框 | `Window` + owner、异步命令 | `AttachCoordinator` | 错误、重试、确认、取消 |
| 三种树模式 | `TreeView`, `HierarchicalDataTemplate` | `ElementTreeViewModel` | T-01..T-05 |
| 树筛选/空状态 | `TextBox` + `ICollectionView` 或树剪枝 | `TreeSearchService` | 搜索时选择状态可恢复 |
| 预览/高亮 | `Image` + `Canvas` overlay | `PreviewViewModel`, `ElementBoundsService` | 树与预览双向选中 |
| 属性检视 | 虚拟化 `DataGrid` / `ListView` + 自定义 editor | `PropertyInspectorViewModel` | A-01..A-08 |
| 来源详情 | `Expander` / detail pane | `PropertyOriginService` | Local/Style/Binding 及完整来源 |
| 绑定查看 | `DataGrid` + state badges | `BindingInspectorViewModel` | Path、DataItem、状态、原始错误 |
| 触发器、事件 | Tab/视图模板 | `TriggerViewModel`, `EventViewModel` | 一致选择/筛选 |
| 下方诊断/输出 | `TabControl`, virtualized rows | `DiagnosticsViewModel` | D-01..D-03 |
| Splitters/工作区 | `Grid`, `GridSplitter`；先不引入复杂 Docking 包 | `WorkspaceLayoutService` | D-04..D-05 |
| 命令面板 | 无边框 Snoop-owned `Window` / `Popup` + 搜索 | `CommandPaletteViewModel` | Ctrl+K、Esc、Enter |
| 截图预览 | WPF `Image` + `Canvas` | `CaptureViewModel` | 目标端采集，异步显示 |
| MCP Dashboard | `UserControl`, `ItemsControl`, `TabControl` | `McpDashboardViewModel` | M-01..M-12 |
| 授权对话框 | Snoop-owned modal `Window`，禁用重复提交 | `PermissionDialogViewModel` | 目标/客户端/工具/参数清晰 |
| Settings | `ContentControl` + `ComboBox` | `SettingsViewModel` | S-01..S-03 |

优先使用原生 WPF 控件和可覆盖 ControlTemplate，不强制引入现代 UI 库。需要第三方 Docking / Fluent 控件时，先验证净依赖大小、net462 运行、暗色、DPI、目标进程资源污染与开源许可。

### 原生启动器与截图的验收口径

- **P-01、P-02**　首次展开目标列表时，启动器发现当前已运行的 WPF 窗口。PID、进程名或窗口标题搜索可与运行时和架构筛选组合使用，筛选时保留 PID 排序。筛掉当前目标会清除选择并禁用挂载和放大，「清除筛选」可一次恢复全部结果。
- **P-03（部分覆盖）**　附加辅助进程运行期间，启动器显示中英文进度，保持界面响应，并暂时禁用会切换目标或发起第二次附加的操作。关闭请求会等本次操作和临时设置清理结束后执行。真实目标进入三栏工作台后的初始选中项仍待验证。
- **P-04（部分覆盖）**　附加辅助进程启动失败或返回非零退出码时，启动器显示失败原因和恢复提示，清除原选择并禁用挂载和放大。点击「重试」会刷新并打开目标列表，由用户重新选择目标。受控的子进程和原生界面回归已覆盖此流程。真实权限受限目标仍待验证。
- **D-07**　在工作台预览中打开截图窗口，可先查看目标画面，或取消并返回。点击「保存」后还可在系统文件对话框中取消。只有确认文件路径后才写入 PNG。导出尺寸由目标的布局尺寸和所选 DPI 决定，取样时计入控件在父级中的偏移与变换。HTML 原型的模拟完成提示对应这里的预览操作。

## 3. 统一的状态与数据流

```text
ProcessSelection  → AttachCoordinator → TargetSession (唯一 AttachmentId)
TargetSession     → ElementTreeService → ElementSelectionService
ElementSelection  ├→ PreviewViewModel   (同一 ElementHandle)
                  ├→ PropertyViewModel  (同一 ElementHandle)
                  ├→ DiagnosticsViewModel (定位至相同对象)
                  └→ StatusBar / Breadcrumb
PropertyEdit      → Validation → InspectionFacade.SetProperty → History → UI refresh
MCP ToolCall      → McpHost.Authorization → Bridge → 同一 InspectionFacade
```

- `ElementSelectionService` 是唯一选中源：树、预览、诊断与 MCP 显示不能各自存一份独立 ID。
- `TargetSession` 含 `AttachmentId`（GUID）、PID、进程启动 UTC 时间/标识、架构、runtime、连接状态和 capability flags。
- 目标退出：撤销授权、废止所有 ElementHandle、清理历史、停止自动订阅；MCP Host 本身可以继续运行。
- WPF MVVM：建议使用已有或轻量 `INotifyPropertyChanged`/`ICommand`，而非为了原型移植 JS state reducer。
- UI 长时间运行：树/属性/日志必须限制集合大小，使用 UI 线程批量同步与增量更新；避免整窗重新生成。

## 4. 属性来源必须符合 WPF 真实语义

`Local`/`Style`/`Binding` 是显示摘要，**不是完整的依赖属性优先级模型**。

真实采集时：

```csharp
var source = DependencyPropertyHelper.GetValueSource(element, dependencyProperty);
var expression = BindingOperations.GetBindingExpressionBase(element, dependencyProperty);
var local = element.ReadLocalValue(dependencyProperty);
var effective = element.GetValue(dependencyProperty);
```

返回 `BaseValueSource`、`IsExpression`、`IsAnimated`、`IsCoerced`、本地原始值、最终生效值及 BindingPath/绑定状态。不要把 `BindingExpression` 误判为单纯 Local 值；显示 `Binding` badge 应基于 Expression 信息。

- `LocalValue` 优先级通常高于样式，而动画/强制值会影响最终值。
- 编辑带绑定的属性时需提示 **SetCurrentValue（保留绑定尽力而为）** 或 **SetValue/SetBinding/ClearValue（改变值源）**，并明确 undo 行为。
- `ActualWidth` 等只读属性不允许写；负宽度/不可解析数值拒绝，不污染 UI/历史。
- 对未实现可靠回滚的复杂依赖属性，显式标记 `canUndo=false`，不承诺全部可撤销。
- 来自 WPF 属性/绑定的原始 API 名称永不翻译；展示文案才本地化。

### 原生属性面板的撤销与重做（A-04）

在属性面板提交编辑后，可用「撤销」恢复上一个值，用「重做」重新应用该次编辑。历史按每个属性面板分别保存，最多保留最近 100 次可回退编辑。切换树节点后，按钮仍作用于被编辑的原对象，当前选择保持不变。撤销会恢复当时的「已编辑」标记和属性修改报告。提交新的有效修改会清空重做记录，格式错误和等值提交会保留重做记录。

目前支持显式设为本地值的简单依赖属性，例如字符串内容、数值、布尔值和枚举。执行前会重新核对目标、值来源和当前值。若目标应用已经替换绑定、设置动画或改写该值，该属性的过期记录会失效。样式、资源表达式、CLR setter 和复杂对象的编辑不进入可撤销历史。

## 5. 全量验收映射

| 原型 ID | 功能模块 | 建议 WPF 端到端用例 |
|---|---|---|
| P-01, P-02 | 发现/筛选 | 假进程源 + 真实进程枚举适配器，PID/Runtime/架构筛选与空状态 |
| P-03, P-04 | 附加成功/失败 | .NET 10 测试 Harness 成功，非管理员目标失败可重试 |
| P-05, P-06, P-07 | 断连/退出/重启 | 旧句柄、历史、授权失效；Same PID 不复用 attachmentId |
| T-01, T-02, T-03 | 树模式、展开、筛选 | Visual/Logical/Automation；懒加载、空过滤 |
| T-04, T-05 | 同步选中、拾取 | 树/预览/来源/状态栏一致，F6 触发拾取 |
| A-01, A-02 | 属性过滤/排序 | Local、Binding、Style、修改标记；排序稳定 |
| A-03, A-04 | 编辑/撤销/重做 | `SaveButton.Content` ↔ 窗口预览，同会话切换后保持 |
| A-05, A-06 | 验证/只读 | 负宽度、ActualWidth 拒绝，无错误历史记录 |
| A-07, A-08 | 来源/绑定/标签 | `GetValueSource` 与 Expression 正确，Tab 保留当前元素 |
| D-01, D-02, D-03 | 诊断/定位 | Binding error 跳 `NameInput.ToolTip`，日志搜索与清空 |
| D-04, D-05 | 布局 | Splitter、折叠、3 预设、布局还原、DPI |
| D-06, D-07 | 命令/截图 | Ctrl+K、Esc、F6；截图/高亮按 DPI 对齐 |
| M-01, M-02 | 服务管理/读取 | 默认不启动；用户启动后 list/get/inspect 有结构化结果与审计 |
| M-03, M-04, M-05 | 只读/拒绝/单次许可 | 只读硬拒绝；单次许可一次性消费且参数固定 |
| M-06, M-07 | 会话权限/撤销 | 客户端身份、process instance、工具集合严格匹配 |
| M-08, M-09, M-10 | 附加授权/竞态/失败 | 人工附加确认、断连取消、Host 错误恢复 |
| M-11, M-12 | 并发与未知客户端 | 待批准授权互斥，未认证请求不弹窗且计入审计 |
| S-01, S-02, S-03 | 主题、i18n、偏好 | 切换不丢 selection/目标；API 名不翻译；重置范围清晰 |

## 6. UI 线程与性能验收

| 情景 | 基准（建议初始门槛，实际需按 P0 校准） |
|---|---|
| 目标 1000+ 节点树 | 优先按需展开、层级分页；滚动不明显卡顿 |
| 多属性视图 | 行虚拟化；搜索取消旧任务，避免频繁全量反射 |
| 底部事件与日志 | 有界队列，批量 UI 刷新；离开 Tab 停止无必要订阅 |
| 高频选中 | 使用取消令牌与 attachmentId，避免旧响应覆盖新选中 |
| 截图 | 速率限制、最大分辨率/大小、传输超时、DPI 元数据 |
| 拖拽分隔线 | 最小宽度、对触摸板/鼠标均可用，持久化之后还能还原 |

目标指标不是本次已测试结果；应先在目标应用建立性能基线，再定义最终数字。`TreeView` 的层级虚拟化需要独立验证，不能假设加一个 `VirtualizingStackPanel` 就能解决所有展开层级。

## 7. 设计 Token 对照

| 名称 | Light | Dark | WPF ResourceKey |
|---|---|---|---|
| 主文字 | `#1A1A1A` | `#FFFFFF` | `SnoopNext.Brush.Text` |
| 辅助文字 | `#5D5D5D` | `#C8C8C8` | `SnoopNext.Brush.TextSecondary` |
| 面板 | `#FFFFFF` | `#2C2C2C` | `SnoopNext.Brush.Panel` |
| 窗口背景 | `#F3F3F3` | `#202020` | `SnoopNext.Brush.Background` |
| 边界 | `#D1D1D1` | `#505050` | `SnoopNext.Brush.Border` |
| 输入表面 | `#FBFBFB` | `#373737` | `SnoopNext.Brush.Control` |
| 悬停 | `#F0F0F0` | `#414141` | `SnoopNext.Brush.Hover` |
| 按下 | `#E5E5E5` | `#343434` | `SnoopNext.Brush.Pressed` |
| 强调 | `#005FB8` | `#60CDFF` | `SnoopNext.Brush.Accent` |
| 强调底色上的文字 | `#FFFFFF` | `#00354A` | `SnoopNext.Brush.OnAccent` |
| 错误 | `#C42B1C` | `#FF99A4` | `SnoopNext.Brush.Error` |

高对比度取 Windows 的窗口、文字、选中背景和选中文字色。焦点使用可见轮廓，关闭操作的通常状态与其他窗口命令一致。

## 8. 等待挂载浮条与设置

- 独立启动器保留单行置顶。左侧品牌区用于移动浮条，中部目标列表显示进程、PID 和窗口标题；F5 刷新列表。
- “挂载”和放大镜分别作用于下拉框中的目标。各自旁边的准星可直接拖到窗口，Esc 取消拾取。两个准星和列表命令分别提供操作说明。
- 进程选择、设置、主检查器共用语义色。工具按钮使用轻量悬停面和键盘焦点，检查器保留分栏、可拖动的分隔线以及紧凑密度。
- 设置把语言选择、属性编辑和提交操作分区。保存、放弃及恢复默认的行为沿用原设置模型。
