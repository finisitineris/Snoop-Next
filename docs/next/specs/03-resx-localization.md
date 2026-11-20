# Snoop Next — RESX + 动态 WPF 绑定层规范 v1.0

## 1. 核心决策

- 文本资源采用 .NET 内置 `.resx`：`Strings.resx` 为 **en-US 兜底资源**，`Strings.zh-CN.resx` 为简体中文；可保留 `Strings.en-US.resx` 显式版本以便翻译 QA，但不得与 neutral 的英文定义互相冲突。
- 初始 UI 语言：默认中文（可配置为「跟随系统」）；`CurrentLanguage` 在 Snoop 的 **LocalizationManager 私有实例**中维护。
- **绝不调用**被注入 WPF 进程中全局性的 `Thread.CurrentThread.CurrentUICulture = ...`，不对目标应用所有线程设置 `DefaultThreadCurrentUICulture`。这可能影响被调试业务应用。
- `CultureInfo` 仅用于 Snoop 自身 RESX 选取、UI 文案格式化以及 Snoop-owned 数据转换。**不翻译** `Button`、`ActualWidth`、`Customer.Name`、`SaveCommand`、堆栈、原始异常与 WPF API 名称。
- 不用 `DynamicResource` 自动假冒 RESX 的热切换；绑定层靠 `INotifyPropertyChanged` 的 `Item[]` 通知更新。对动态格式化文本/复数另加专用 `IMultiValueConverter` 或格式化 ViewModel。

## 2. 模块组成

```text
Snoop.Next.Localization/
  Strings.resx                     # neutral en-US fallback
  Strings.zh-CN.resx
  LocalizationManager.cs           # INotifyPropertyChanged indexer
  LocExtension.cs                  # {local:Loc Key} → Binding
  LocalizationKeyAnalyzer.cs      # 可选：检查缺失 Key（仅 build-time）
  CultureOption.cs                 # en-US / zh-CN / system
Tests/Localization.Tests/
  ResourceParityTests.cs
  CultureIsolationTests.cs
  DynamicBindingTests.cs
```

推荐最小接口：

```csharp
public interface ILocalizationService : INotifyPropertyChanged
{
    CultureInfo UICulture { get; }
    string this[string key] { get; }
    string Format(string key, params object[] args);
    void SetLanguage(string languageTag);
}
```

- `SetLanguage` 仅在 Snoop 自身 Dispatcher 上执行；由设置模块保存用户选择。
- Key 找不到时：首先回退 `Strings.resx` 英文，再显示易诊断的 `[Missing: Key]`，并记录一次去重后的调试警告。
- RESX key 为稳定语义标识，禁止使用中文/英文全文本作 Key。
- 应用重启：读取设置；运行时切换：触发 `PropertyChanged("Item[]")`（通知 WPF 索引器绑定更新），并通知需要格式化刷新/无障碍内容的 ViewModel。
- 国际化数据**不**经过 MCP 返回而篡改目标原始属性值。MCP `message` 可依客户端 UI 语言本地化，但 `errorCode`、枚举、tool 名称不翻译。

## 3. XAML 示例（最低 TFM 下验证）

```xml
<Window
    xmlns:local="clr-namespace:Snoop.Next.Localization;assembly=Snoop.Next.Localization"
    Title="{local:Loc Shell.Title}">
  <DockPanel>
    <TextBlock Text="{local:Loc Inspector.Tree.Title}" />
    <Button Content="{local:Loc Process.Attach}" />
    <MenuItem Header="{local:Loc Menu.File}" />
  </DockPanel>
</Window>
```

`LocExtension` 返回绑定对象，典型属性类型为 `string`/`object`。**Style Setter、Triggers、Freezable、设计时设计器、数据模板卸载重载须单独验证**；这些宿主若不能可靠接收该 MarkupExtension，则使用常规 `Binding Source={x:Static ...}, Path=[Key]` 或 `LocText` 附加行为，不能依赖偶然工作。

## 4. 资源清单与提取策略

HTML v1.1 的 `view.js` 定义了中英文词典，**只作为 UI 文案盘点**，不原样照搬 JS 的 key 命名，也不要把样例数据作为永久产品文案。

| 类别 | RESX Key 前缀 | 示例键 |
|---|---|---|
| Shell/Nav/Menu | `Shell.*`, `Nav.*`, `Menu.*` | `Shell.Title`, `Nav.Inspector`, `Menu.Tools` |
| 进程/会话 | `Process.*`, `Target.*` | `Process.Attach`, `Target.Disconnected`, `Target.AttachFailed` |
| 树/拾取 | `Tree.*`, `Picker.*` | `Tree.SearchPlaceholder`, `Picker.Active` |
| 属性/编辑 | `Property.*`, `Editor.*` | `Property.ValueOrigin`, `Editor.ReadOnly`, `Editor.Validation.Width` |
| 绑定/诊断 | `Binding.*`, `Diagnostic.*` | `Binding.Path`, `Diagnostic.Navigate` |
| 布局/命令 | `Workspace.*`, `Command.*` | `Workspace.ResetLayout`, `Command.Search` |
| MCP/权限 | `Mcp.*`, `Permission.*` | `Mcp.Start`, `Permission.AllowOnce`, `Permission.Pending` |
| 设置 | `Settings.*`, `Theme.*` | `Settings.Language`, `Theme.Dark` |
| 体验/可访问性 | `Accessibility.*`, `Error.*` | `Accessibility.Refresh`, `Error.TargetExited` |

第一批所有显式 UI 文案必须纳入资源库，包括 ToolTip、模态按钮、错误/警告信息、快捷键帮助、屏幕阅读器标签与调用审计的本地显示。调试日志的原始 Payload/堆栈不可本地化。

## 5. 本地化与 WPF 目标隔离验收

| 用例 | 步骤 | 成功条件 |
|---|---|---|
| I18N-01 | `zh-CN → en-US → zh-CN` | 菜单、Tab、Tooltip、弹窗、审计展示同步刷新，不重新附加目标 |
| I18N-02 | .NET Framework 4.6.2 Harness 运行并切换 Snoop 语言 | 目标线程 CurrentUICulture、`Application.Resources`、业务控件值保持不变 |
| I18N-03 | 删除某中文翻译 | 英文资源兜底，没有空白按钮，不抛异常 |
| I18N-04 | 长英文/中文、125–200% DPI | 主要命令不裁切，窗口 1280×720 可操作 |
| I18N-05 | MCP 已启动 + 属性已修改，切换 UI 语言 | Permission、TargetSession、History 不丢失 |
| I18N-06 | 新增 RESX key 没有另一个语言版本 | CI 报资源完整性警告/错误（按优先级） |
| I18N-07 | 存在数字、日期、复合格式 | 只格式化 Snoop UI，目标原始值不因语言改变 |
| I18N-08 | 运行多 UI Dispatcher/窗口 | Snoop 每个 Window 均更新，无事件泄漏 |

## 6. 与主题分离

国际化服务只返回内容字符串；ThemeManager 只管理私有 `ResourceDictionary`，不共用所谓的“语言 Theme”。不把字符串放进颜色字典。布局密度属于工作台设置，和语言独立。

## 7. 示例源码

随交付附 `localization-sample/`：包含语言管理器、XAML MarkupExtension 和可解析的 RESX 资源。该源码为**设计样例**，需在 Windows net462 与 net10/11 构建及 UI 热切换测试后才可入生产主干。

## 8. 参考

- WPF 数据绑定：<https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/>
- WPF Markup Extensions：<https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/markup-extensions-and-wpf-xaml>
- .NET ResourceManager：<https://learn.microsoft.com/en-us/dotnet/api/system.resources.resourcemanager>