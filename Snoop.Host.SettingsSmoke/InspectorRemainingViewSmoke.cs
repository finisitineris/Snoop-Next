namespace Snoop.Host.SettingsSmoke;

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Controls.ValueEditors.Details;
using Snoop.Data.Tree;
using Snoop.Infrastructure.Diagnostics;
using Snoop.Infrastructure.Diagnostics.Providers;
using Snoop.Localization;
using Snoop.PowerShell;
using Snoop.Views;
using Snoop.Views.BehaviorsTab;
using Snoop.Views.MethodsTab;
using Snoop.Views.TriggersTab;
using Snoop.Windows;

internal static class InspectorRemainingViewSmoke
{
    public static void Verify()
    {
        var localization = InspectorLocalization.Default;
        var originalLanguage = localization.LanguageTag;
        localization.SetLanguage("zh-CN");
        var errorDialog = new ErrorDialog();
        var valueEditor = new ValueEditorDetailsWindow();
        try
        {
            var methods = new MethodsControl();
            var useDataContext = (TextBlock)Find<CheckBox>(methods, "checkBoxUseDataContext").Content;
            var highlight = new HighlightSettingsView();
            var closeHighlight = Find<Button>(highlight, "Close");
            var propertyInspector = new PropertyInspector();
            var defaultsBadge = (TextBlock)Find<ToggleButton>(propertyInspector, "defaultsToggle").Content;
            var uncommonBadge = (TextBlock)Find<ToggleButton>(propertyInspector, "uncommonToggle").Content;
            var triggers = new TriggersView().Children.OfType<TextBlock>().Single();
            var behaviors = new BehaviorsView().Children.OfType<TextBlock>().Single();
            var shell = new EmbeddedShellView();
            var autoExpand = (TextBlock)Find<CheckBox>(shell, "autoExpandCheckBox").Content;
            var previewer = new Previewer();
            var screenshot = Find<Grid>(previewer, "PreviewToolbar").Children.OfType<Button>()
                .Single(button => button.Command == Previewer.ScreenshotCommand);
            var screenshotTip = (TextBlock)screenshot.ToolTip;

            var diagnostics = new DiagnosticsView();
            var providerTemplate = (DataTemplate)diagnostics.Resources[new DataTemplateKey(typeof(DiagnosticProvider))];
            using var provider = new FreezeFreezablesDiagnosticProvider();
            var providerCheckBox = (CheckBox)providerTemplate.LoadContent();
            providerCheckBox.DataContext = provider;
            var providerLabel = (TextBlock)providerCheckBox.Content;
            var diagnosticItem = new DiagnosticItem(provider, "Original diagnostic", area: DiagnosticArea.Performance, level: DiagnosticLevel.Warning);
            var defaultDiagnostic = new DiagnosticItem(provider);
            var buttonWithResource = new Button();
            buttonWithResource.Resources.Add("SmokeBrush", new SolidColorBrush(Colors.Red));
            using var treeService = TreeService.From(TreeType.Visual);
            using var treeItem = new TreeItem(buttonWithResource, null, treeService);
            var generatedDiagnostic = provider.GetDiagnosticItems(treeItem).Single();
            const string rawDescription = "Freezing the resource 'SmokeBrush' can save memory and increase performance.";
            var columns = ((GridView)Find<ListView>(diagnostics, "diagnostics").View).Columns;
            var levelLabel = CreateDiagnosticLabel(columns[1], diagnosticItem);
            var areaLabel = CreateDiagnosticLabel(columns[4], diagnosticItem);
            var customName = CreateDiagnosticLabel(columns[2], diagnosticItem);
            var defaultName = CreateDiagnosticLabel(columns[2], defaultDiagnostic);
            var defaultDescription = CreateDiagnosticLabel(columns[3], defaultDiagnostic);
            var generatedDescription = CreateDiagnosticLabel(columns[3], generatedDiagnostic);
            DrainDispatcher();

            RequireText(useDataContext.Text, "使用 DataContext 属性", "Methods DataContext action");
            RequireText(closeHighlight.Content as string, "关闭", "Highlight close button");
            RequireText(defaultsBadge.Text, "默认", "Default properties toggle");
            RequireText(uncommonBadge.Text, "少用", "Uncommon properties toggle");
            RequireText(triggers.Text, "元素没有触发器", "Empty triggers view");
            RequireText(behaviors.Text, "元素没有行为", "Empty behaviors view");
            RequireText(autoExpand.Text, "自动展开", "PowerShell auto-expand action");
            RequireText(screenshotTip.Text, "将当前预览保存到文件。", "Preview screenshot tooltip");
            RequireText(errorDialog.Title, "错误 - Snoop", "Error dialog title");
            RequireText(valueEditor.Title, "值编辑器", "Value editor title");
            RequireText(providerLabel.Text, "冻结 Freezable", "Diagnostics provider caption");
            RequireText(levelLabel.Text, "警告", "Diagnostics level caption");
            RequireText(areaLabel.Text, "性能", "Diagnostics area caption");
            RequireText(customName.Text, "Original diagnostic", "Custom diagnostic name");
            RequireText(defaultName.Text, "冻结 Freezable", "Default diagnostic name");
            RequireText(defaultDescription.Text, "应冻结 Freezable 以节省内存并提升性能。", "Default diagnostic description");
            RequireText(generatedDescription.Text, "冻结资源“SmokeBrush”可以减少内存占用并提升性能。", "Generated diagnostic description");
            RequireText(generatedDiagnostic.Description, rawDescription, "Raw generated diagnostic description");
            RequireText(provider.Name, "Freeze freezables", "Diagnostics provider export name");

            localization.SetLanguage("en-US");
            DrainDispatcher();

            RequireText(useDataContext.Text, "Use DataContext Property", "Methods DataContext action");
            RequireText(closeHighlight.Content as string, "Close", "Highlight close button");
            RequireText(defaultsBadge.Text, "D", "Default properties toggle");
            RequireText(uncommonBadge.Text, "U", "Uncommon properties toggle");
            RequireText(triggers.Text, "The element has no triggers", "Empty triggers view");
            RequireText(behaviors.Text, "The element has no behaviors", "Empty behaviors view");
            RequireText(autoExpand.Text, "Auto-expand", "PowerShell auto-expand action");
            RequireText(screenshotTip.Text, "Save the current preview to a file.", "Preview screenshot tooltip");
            RequireText(errorDialog.Title, "Error occurred - Snoop", "Error dialog title");
            RequireText(valueEditor.Title, "Value editor", "Value editor title");
            RequireText(providerLabel.Text, "Freeze freezables", "Diagnostics provider caption");
            RequireText(levelLabel.Text, "Warning", "Diagnostics level caption");
            RequireText(areaLabel.Text, "Performance", "Diagnostics area caption");
            RequireText(defaultName.Text, "Freeze freezables", "Default diagnostic name");
            RequireText(defaultDescription.Text, provider.Description, "Default diagnostic description");
            RequireText(generatedDescription.Text, rawDescription, "Generated diagnostic description");
            RequireText(diagnosticItem.Name, "Original diagnostic", "Raw diagnostic name");
            RequireText(defaultDiagnostic.Description, provider.Description, "Raw diagnostic description");
            Console.WriteLine("PASS: remaining inspector views initialize in Chinese and refresh in English");
        }
        finally
        {
            errorDialog.Close();
            valueEditor.Close();
            localization.SetLanguage(originalLanguage);
        }
    }

    private static T Find<T>(FrameworkElement parent, string name)
        where T : class
    {
        return parent.FindName(name) as T
               ?? throw new InvalidOperationException("Missing inspector control " + name);
    }

    private static TextBlock CreateDiagnosticLabel(GridViewColumn column, DiagnosticItem item)
    {
        var content = (StackPanel)column.CellTemplate.LoadContent();
        content.DataContext = item;
        return content.Children.OfType<TextBlock>().Single();
    }

    private static void DrainDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    private static void RequireText(string? actual, string expected, string label)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label} expected '{expected}', got '{actual}'");
        }
    }
}
