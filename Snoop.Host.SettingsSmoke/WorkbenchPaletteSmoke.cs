namespace Snoop.Host.SettingsSmoke;

using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Data.Tree;
using Snoop.Localization;
using Snoop.Windows;

internal static class WorkbenchPaletteSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var palette = inspector.FindName("WorkbenchCommandPalette") as Popup
            ?? throw new InvalidOperationException("Missing workbench command palette.");
        var button = Find<Button>(inspector, "CommandPaletteButton");
        var surface = Find<Border>(inspector, "CommandPaletteSurface");
        var query = Find<TextBox>(inspector, "WorkbenchCommandQuery");
        var results = Find<ListBox>(inspector, "WorkbenchCommandResults");
        var empty = Find<TextBlock>(inspector, "WorkbenchCommandNoResults");
        var propertyInspector = Find<PropertyInspector>(inspector, "PropertyInspector");
        var showDiagnostics = Find<MenuItem>(inspector, "ShowDiagnosticsPaneItem");
        var diagnostics = Find<TabControl>(inspector, "DiagnosticsWorkbenchPane");
        var diagnosticsTab = Find<TabItem>(inspector, "WorkbenchDiagnosticsTab");
        var outputTab = Find<TabItem>(inspector, "WorkbenchOutputTab");

        Require(ReferenceEquals(palette.Child, surface) && ReferenceEquals(palette.PlacementTarget, button)
                && surface.IsAncestorOf(query) && surface.IsAncestorOf(results),
            "The command palette must anchor to its opener and contain its search and results.");
        Require(ReferenceEquals(button.Command, SnoopUI.OpenCommandPaletteCommand)
                && SnoopUI.OpenCommandPaletteCommand.InputGestures.OfType<KeyGesture>()
                    .Any(gesture => gesture.Key == Key.K && gesture.Modifiers == ModifierKeys.Control),
            "The command bar button and Ctrl+K must open the same command palette.");
        Require(results.DisplayMemberPath == "Caption", "The command palette must display localized command captions.");

        var savedLanguage = InspectorLocalization.Default.LanguageTag;
        var savedSelection = inspector.CurrentSelection;
        var savedFilter = propertyInspector.FilterString;
        var savedDiagnosticsVisibility = showDiagnostics.IsChecked;
        var savedDiagnosticsIndex = diagnostics.SelectedIndex;
        using var treeService = TreeService.From(TreeType.Visual);
        using var selection = new TreeItem(new Button { Content = "Target selection" }, null, treeService);
        // The hidden input source provides a PresentationSource for routed KeyEventArgs.
        using var keySource = new HwndSource(new HwndSourceParameters("Snoop workbench palette smoke")
        {
            Width = 1,
            Height = 1,
            WindowStyle = 0,
        });
        inspector.TreeItems.Add(selection);

        try
        {
            inspector.CurrentSelection = selection;
            propertyInspector.FilterString = "Width";
            SetDiagnosticsVisible(false);

            InspectorLocalization.Default.SetLanguage("en-US");
            Open();
            Require(button.Content as string == "Command palette", "Missing English command palette button label.");
            VerifyCaptions("Element tree", "Properties", "Diagnostics", "Events", "Preview", "Output");
            SetQuery("pREv");
            VerifyCaptions("Preview");
            SetQuery("A command that cannot match");
            VerifyCaptions();
            Require(empty.Visibility == Visibility.Visible, "An empty command search must show an empty state.");
            SendKey(Key.Enter);
            Require(OpenRequested() && !showDiagnostics.IsChecked, "Enter on empty results must leave the palette open.");
            SendKey(Key.Escape);
            Require(!OpenRequested() && propertyInspector.FilterString == "Width" && !showDiagnostics.IsChecked,
                "Escape must dismiss the palette and preserve the property filter and pane state.");

            InspectorLocalization.Default.SetLanguage("zh-CN");
            Open();
            Require(button.Content as string == "快速命令" && query.Text.Length == 0,
                "Reopening the palette must use the current language and start with a fresh search.");
            VerifyCaptions("元素树", "属性", "诊断", "事件", "预览", "输出");
            SetQuery("诊断");
            VerifyCaptions("诊断");
            Require(empty.Visibility == Visibility.Collapsed && results.SelectedIndex == 0,
                "A matching search must select a result and hide the empty state.");
            SendKey(Key.Enter);
            Require(!OpenRequested() && showDiagnostics.IsChecked && diagnostics.Visibility == Visibility.Visible
                    && ReferenceEquals(diagnostics.SelectedItem, diagnosticsTab),
                "Enter must close the palette and reveal the Diagnostics tab.");

            SetDiagnosticsVisible(false);
            Open();
            SetQuery("输出");
            VerifyCaptions("输出");
            SendKey(Key.Enter);
            Require(!OpenRequested() && showDiagnostics.IsChecked && diagnostics.Visibility == Visibility.Visible
                    && ReferenceEquals(diagnostics.SelectedItem, outputTab),
                "Enter must close the palette and reveal the Output tab.");
            Require(ReferenceEquals(inspector.CurrentSelection, selection) && selection.IsSelected
                    && propertyInspector.FilterString == "Width",
                "Executing a palette navigation command must preserve selection and the property filter.");
        }
        finally
        {
            palette.IsOpen = false;
            SetDiagnosticsVisible(savedDiagnosticsVisibility);
            diagnostics.SelectedIndex = savedDiagnosticsIndex;
            inspector.CurrentSelection = savedSelection;
            inspector.TreeItems.Remove(selection);
            propertyInspector.FilterString = savedFilter;
            InspectorLocalization.Default.SetLanguage(savedLanguage);
            DrainDispatcher();
        }

        Console.WriteLine("PASS: workbench command palette filters localized commands and isolates Escape and Enter");

        void Open()
        {
            SnoopUI.OpenCommandPaletteCommand.Execute(null, inspector);
            DrainDispatcher();
            Require(OpenRequested(), "The command palette opener must request an open popup.");
        }

        bool OpenRequested()
        {
            // An unloaded Popup can defer the effective IsOpen change.
            return palette.IsOpen || palette.ReadLocalValue(Popup.IsOpenProperty) is true;
        }

        void SetQuery(string text)
        {
            query.Text = text;
            DrainDispatcher();
        }

        void VerifyCaptions(params string[] expected)
        {
            var actual = results.Items.Cast<object>()
                .Select(item => TypeDescriptor.GetProperties(item).Find(results.DisplayMemberPath, false)?.GetValue(item) as string);
            Require(actual.OrderBy(caption => caption, StringComparer.Ordinal)
                    .SequenceEqual(expected.OrderBy(caption => caption, StringComparer.Ordinal)),
                "Unexpected results for the command palette query '" + query.Text + "'.");
        }

        void SendKey(Key key)
        {
            var keyEvent = new KeyEventArgs(Keyboard.PrimaryDevice, keySource, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            query.RaiseEvent(keyEvent);
            DrainDispatcher();
            Require(keyEvent.Handled, "The command palette must consume " + key + " while it is open.");
        }

        void SetDiagnosticsVisible(bool visible)
        {
            if (showDiagnostics.IsChecked != visible)
            {
                showDiagnostics.IsChecked = visible;
                showDiagnostics.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, showDiagnostics));
                DrainDispatcher();
            }
        }
    }

    private static T Find<T>(SnoopUI inspector, string name)
        where T : FrameworkElement
    {
        return inspector.FindName(name) as T
            ?? throw new InvalidOperationException("Missing workbench command palette element " + name + ".");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void DrainDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }
}
