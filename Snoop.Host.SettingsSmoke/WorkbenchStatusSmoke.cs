namespace Snoop.Host.SettingsSmoke;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Data.Tree;
using Snoop.Localization;
using Snoop.Windows;

internal static class WorkbenchStatusSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var content = Find<Grid>(inspector, "WorkbenchStatusContent");
        var statusBar = Find<StatusBar>(inspector, "WorkbenchStatusBar");
        var chrome = Find<DockPanel>(inspector, "WorkbenchChrome");
        var targetLabel = Find<TextBlock>(inspector, "WorkbenchTargetLabel");
        var targetValue = Find<TextBlock>(inspector, "WorkbenchTargetValue");
        var selectionLabel = Find<TextBlock>(inspector, "WorkbenchSelectionLabel");
        var selectionValue = Find<TextBlock>(inspector, "WorkbenchSelectionValue");
        Require(content.ColumnDefinitions.Count == 2 && content.RowDefinitions.Count == 2,
            "The workbench status must group its information in two columns and two rows.");
        Require(targetValue.TextTrimming != TextTrimming.None && selectionValue.TextTrimming != TextTrimming.None,
            "The workbench target and selection must support text trimming.");

        var savedLanguage = InspectorLocalization.Default.LanguageTag;
        var savedRoot = inspector.RootTreeItem;
        var savedSelection = inspector.CurrentSelection;
        var rootProperty = typeof(SnoopUI).GetProperty(nameof(SnoopUI.RootTreeItem))
            ?? throw new InvalidOperationException("Missing the inspector root property for the workbench status fixture.");
        using var treeService = TreeService.From(TreeType.Visual);
        var element = new Button { Name = "InitialStatusSelection" };
        using var selection = new TreeItem(element, null, treeService).Reload();
        using var process = Process.GetCurrentProcess();
        var processIdentity = $"{process.ProcessName} · PID {process.Id.ToString(CultureInfo.InvariantCulture)}";
        inspector.TreeItems.Add(selection);

        try
        {
            inspector.CurrentSelection = null;
            rootProperty.SetValue(inspector, null);

            InspectorLocalization.Default.SetLanguage("en-US");
            DrainDispatcher();
            VerifyStatus("Target:", "No target", "Selected:", "No selection");

            InspectorLocalization.Default.SetLanguage("zh-CN");
            DrainDispatcher();
            VerifyStatus("目标：", "无目标", "选中：", "未选择元素");

            rootProperty.SetValue(inspector, selection);
            inspector.CurrentSelection = selection;
            DrainDispatcher();
            VerifyStatus("目标：", processIdentity, "选中：", selection.DisplayName);

            InspectorLocalization.Default.SetLanguage("en-US");
            element.Name = new string('W', 120);
            selection.Reload();
            DrainDispatcher();
            VerifyStatus("Target:", processIdentity, "Selected:", selection.DisplayName);
            Require(GetTooltipText(targetValue.ToolTip) == processIdentity,
                "The target tooltip must retain the full process identity.");
            Require(GetTooltipText(selectionValue.ToolTip) == selection.DisplayName,
                "The selection tooltip must retain the full display name.");

            WorkbenchLayoutSmoke.Arrange(chrome, new Rect(0, 0, 480, 320));
            Require(content.ActualWidth > 0 && content.ActualWidth <= statusBar.ActualWidth + 1,
                "The workbench status content must fit its status bar at a width of 480 DIPs.");
            Require(targetValue.ActualWidth > 0 && selectionValue.ActualWidth > 0,
                "The target and selection values must both receive space in the narrow workbench.");

            VerifyFocusLink(inspector, "WorkbenchSelectFocusLink", SnoopUI.SelectFocusCommand, nameof(SnoopUI.CurrentFocus), inspector.CurrentFocus);
            VerifyFocusLink(inspector, "WorkbenchSelectFocusScopeLink", SnoopUI.SelectFocusScopeCommand, nameof(SnoopUI.CurrentFocusScope), inspector.CurrentFocusScope);

            inspector.CurrentSelection = null;
            DrainDispatcher();
            VerifyStatus("Target:", processIdentity, "Selected:", "No selection");
            rootProperty.SetValue(inspector, null);
            DrainDispatcher();
            VerifyStatus("Target:", "No target", "Selected:", "No selection");
        }
        finally
        {
            rootProperty.SetValue(inspector, savedRoot);
            inspector.CurrentSelection = savedSelection;
            inspector.TreeItems.Remove(selection);
            InspectorLocalization.Default.SetLanguage(savedLanguage);
            DrainDispatcher();
        }

        Console.WriteLine("PASS: workbench status tracks the target and selection and preserves focus commands");

        void VerifyStatus(string expectedTargetLabel, string expectedTarget, string expectedSelectionLabel, string expectedSelection)
        {
            Require(targetLabel.Text == expectedTargetLabel, "Incorrect target label in the workbench status.");
            Require(selectionLabel.Text == expectedSelectionLabel, "Incorrect selection label in the workbench status.");
            Require(targetValue.Text == expectedTarget, $"Expected workbench target '{expectedTarget}', got '{targetValue.Text}'.");
            Require(selectionValue.Text == expectedSelection, $"Expected workbench selection '{expectedSelection}', got '{selectionValue.Text}'.");
        }
    }

    private static void VerifyFocusLink(SnoopUI inspector, string name, ICommand command, string bindingPath, object? parameter)
    {
        var link = Find<NoFocusHyperlink>(inspector, name);
        Require(!link.Focusable && ReferenceEquals(link.Command, command),
            $"{name} must preserve its focus-free selection command.");
        var binding = BindingOperations.GetBinding(link, Hyperlink.CommandParameterProperty);
        Require(binding?.Path?.Path == bindingPath && ReferenceEquals(link.CommandParameter, parameter),
            $"{name} must pass the corresponding inspector focus value to its command.");
    }

    private static string? GetTooltipText(object? tooltip)
    {
        var content = tooltip is ToolTip control ? control.Content : tooltip;

        return content switch
        {
            TextBlock textBlock => textBlock.Text,
            string text => text,
            _ => null,
        };
    }

    private static void DrainDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    private static T Find<T>(SnoopUI inspector, string name)
        where T : class
    {
        return inspector.FindName(name) as T
            ?? throw new InvalidOperationException("Missing workbench status element " + name + ".");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
