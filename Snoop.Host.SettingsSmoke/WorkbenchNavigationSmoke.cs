namespace Snoop.Host.SettingsSmoke;

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Data.Tree;
using Snoop.Localization;
using Snoop.Windows;

internal static class WorkbenchNavigationSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var rail = inspector.FindName("WorkbenchNavigationRail") as Border
            ?? throw new InvalidOperationException("Missing workbench navigation rail.");
        var grid = Find<Grid>(inspector, "WorkbenchGrid");
        var tree = Find<ProperTreeView>(inspector, "Tree");
        var treeColumn = Find<ColumnDefinition>(inspector, "TreePaneColumn");
        var propertiesColumn = Find<ColumnDefinition>(inspector, "InspectorPaneColumn");
        var diagnosticsRow = Find<RowDefinition>(inspector, "DiagnosticsPaneRow");
        var properties = Find<TabControl>(inspector, "PropertiesWorkbenchPane");
        var diagnostics = Find<TabControl>(inspector, "DiagnosticsWorkbenchPane");
        var showTree = Find<MenuItem>(inspector, "ShowTreePaneItem");
        var showProperties = Find<MenuItem>(inspector, "ShowPropertiesPaneItem");
        var showDiagnostics = Find<MenuItem>(inspector, "ShowDiagnosticsPaneItem");
        var propertiesTab = Find<TabItem>(inspector, "WorkbenchPropertiesTab");
        var dataContextTab = LogicalTreeHelper.GetParent(Find<PropertyInspector>(inspector, "DataContextPropertyGrid")) as TabItem
            ?? throw new InvalidOperationException("Missing workbench Data Context tab.");
        var eventsTab = Find<TabItem>(inspector, "WorkbenchEventsTab");
        var diagnosticsTab = Find<TabItem>(inspector, "WorkbenchDiagnosticsTab");
        var outputTab = Find<TabItem>(inspector, "WorkbenchOutputTab");
        var previewPane = Find<FrameworkElement>(inspector, "WorkbenchPreviewPane");
        var treeButton = Find<Button>(inspector, "NavigateTreeButton");
        var propertiesButton = Find<Button>(inspector, "NavigatePropertiesButton");
        var diagnosticsButton = Find<Button>(inspector, "NavigateDiagnosticsButton");
        var eventsButton = Find<Button>(inspector, "NavigateEventsButton");
        var previewButton = Find<Button>(inspector, "NavigatePreviewButton");
        var buttons = new[] { treeButton, propertiesButton, diagnosticsButton, eventsButton, previewButton };
        var focusVisual = inspector.FindResource("SnoopNext.WorkbenchFocusVisual");
        var chrome = Find<DockPanel>(inspector, "WorkbenchChrome");
        var frame = SystemParameters.WindowNonClientFrameThickness;
        Arrange(chrome, inspector.MinWidth - frame.Left - frame.Right, inspector.MinHeight - frame.Top - frame.Bottom);
        Require(rail.Visibility == Visibility.Visible && DockPanel.GetDock(rail) == Dock.Left && !IsWithin(rail, grid),
            "The rail should remain visible and docked outside the collapsible workbench grid.");
        Require(Math.Abs(rail.ActualWidth - rail.Width) < 1 && rail.ActualHeight > 0,
            "The rail must keep its width at the narrow workbench size. Actual width: " + rail.ActualWidth + ".");
        foreach (var button in buttons)
        {
            Require(IsWithin(button, rail), button.Name + " is missing from the navigation rail.");
            Require(button.Focusable && button.IsTabStop, button.Name + " must be reachable by keyboard.");
            Require(ReferenceEquals(button.FocusVisualStyle, focusVisual),
                button.Name + " must use the workbench focus style.");
            Require(button.Content is Path { Stroke: SolidColorBrush },
                button.Name + " must render a themed navigation icon.");
        }

        Require(showTree.IsChecked && showProperties.IsChecked && showDiagnostics.IsChecked,
            "The navigation fixture requires all workbench panes to start visible.");
        Arrange(chrome, 1200, 700);
        var savedTreeWidth = treeColumn.Width;
        var savedInspectorWidth = propertiesColumn.Width;
        var savedDiagnosticsHeight = diagnosticsRow.Height;
        var savedLanguage = InspectorLocalization.Default.LanguageTag;
        var savedSelection = inspector.CurrentSelection;
        var savedPropertiesTab = properties.SelectedIndex;
        var savedDiagnosticsTab = diagnostics.SelectedIndex;
        var propertyInspector = Find<PropertyInspector>(inspector, "PropertyInspector");
        var savedFilter = propertyInspector.FilterString;
        var preview = Find<Previewer>(inspector, "PreviewArea");
        var previewActive = preview.IsActive;
        Require(IsWithin(preview, previewPane) && previewPane.Focusable,
            "Preview navigation must have a focusable center pane containing the existing preview.");
        using var treeService = TreeService.From(TreeType.Visual);
        var target = new Button { Content = "Edited value" };
        using var selection = new TreeItem(target, null, treeService);
        inspector.TreeItems.Add(selection);

        try
        {
            VerifyLabels(buttons, "en-US", "Element tree", "Properties", "Diagnostics", "Events", "Preview");
            VerifyLabels(buttons, "zh-CN", "元素树", "属性", "诊断", "事件", "预览");
            inspector.CurrentSelection = selection;
            propertyInspector.FilterString = "Width";
            properties.SelectedItem = dataContextTab;
            diagnostics.SelectedItem = outputTab;
            MoveSplitter("GridSplitter", 42, 0);
            MoveSplitter("PropertiesSplitter", -37, 0);
            MoveSplitter("DiagnosticsSplitter", 0, -45);
            var customTreeWidth = treeColumn.ActualWidth;
            var customPropertiesWidth = propertiesColumn.ActualWidth;
            var customDiagnosticsHeight = diagnosticsRow.ActualHeight;

            SetPaneVisible(showTree, false);
            SetPaneVisible(showProperties, false);
            SetPaneVisible(showDiagnostics, false);
            Require(rail.Visibility == Visibility.Visible && treeButton.IsEnabled,
                "The navigation rail should remain usable with every pane hidden.");

            Click(treeButton);
            VerifyPanes(true, false, false);
            Require(ReferenceEquals(properties.SelectedItem, dataContextTab) && ReferenceEquals(diagnostics.SelectedItem, outputTab),
                "Opening the element tree changed a tab.");

            SetPaneVisible(showTree, false);
            Click(propertiesButton);
            VerifyPanes(false, true, false);
            Require(ReferenceEquals(properties.SelectedItem, propertiesTab) && ReferenceEquals(diagnostics.SelectedItem, outputTab),
                "Properties navigation did not select only the Properties tab.");

            properties.SelectedItem = dataContextTab;
            SetPaneVisible(showProperties, false);
            Click(eventsButton);
            VerifyPanes(false, false, true);
            Require(ReferenceEquals(properties.SelectedItem, dataContextTab) && ReferenceEquals(diagnostics.SelectedItem, eventsTab),
                "Events navigation must open the diagnostics dock without changing the Properties tabs.");

            Click(diagnosticsButton);
            VerifyPanes(false, false, true);
            Require(ReferenceEquals(properties.SelectedItem, dataContextTab) && ReferenceEquals(diagnostics.SelectedItem, diagnosticsTab),
                "Diagnostics navigation did not select only the Diagnostics tab.");

            SetPaneVisible(showDiagnostics, false);
            SetPaneVisible(showProperties, true);
            Click(previewButton);
            Click(treeButton);
            VerifyPanes(true, true, false);
            Require(ReferenceEquals(properties.SelectedItem, dataContextTab) && ReferenceEquals(diagnostics.SelectedItem, diagnosticsTab),
                "Preview navigation must leave the folded diagnostics dock and its selected tab alone.");
            Require(ReferenceEquals(inspector.CurrentSelection, selection) && selection.IsSelected
                    && ReferenceEquals(propertyInspector.RootTarget, target) && ReferenceEquals(preview.Target, target)
                    && previewPane.Visibility == Visibility.Visible && Equals(target.Content, "Edited value")
                    && propertyInspector.FilterString == "Width" && preview.IsActive == previewActive,
                "Navigation lost the selected element, its value, the property filter or the preview preference.");
            SetPaneVisible(showDiagnostics, true);
            Arrange(chrome, 1200, 700);
            Require(Math.Abs(treeColumn.ActualWidth - customTreeWidth) < 1
                    && Math.Abs(propertiesColumn.ActualWidth - customPropertiesWidth) < 1
                    && Math.Abs(diagnosticsRow.ActualHeight - customDiagnosticsHeight) < 1,
                "Navigation lost a cached splitter size.");
        }
        finally
        {
            SetPaneVisible(showTree, true);
            SetPaneVisible(showProperties, true);
            SetPaneVisible(showDiagnostics, true);
            treeColumn.Width = savedTreeWidth;
            propertiesColumn.Width = savedInspectorWidth;
            diagnosticsRow.Height = savedDiagnosticsHeight;
            inspector.CurrentSelection = savedSelection;
            inspector.TreeItems.Remove(selection);
            properties.SelectedIndex = savedPropertiesTab;
            diagnostics.SelectedIndex = savedDiagnosticsTab;
            propertyInspector.FilterString = savedFilter;
            InspectorLocalization.Default.SetLanguage(savedLanguage);
        }

        Console.WriteLine("PASS: workbench navigation opens existing views and preserves state and splitter ratios");

        void VerifyPanes(bool treeVisible, bool propertiesVisible, bool diagnosticsVisible)
        {
            Require(showTree.IsChecked == treeVisible && IsShown(tree) == treeVisible
                    && showProperties.IsChecked == propertiesVisible && properties.Visibility == VisibleWhen(propertiesVisible)
                    && showDiagnostics.IsChecked == diagnosticsVisible && diagnostics.Visibility == VisibleWhen(diagnosticsVisible),
                "The View menu and workbench pane visibility should stay in sync.");
        }

        void MoveSplitter(string name, double x, double y)
        {
            var splitter = Find<GridSplitter>(inspector, name);
            ((ITransformProvider)new GridSplitterAutomationPeer(splitter)).Move(x, y);
            Arrange(chrome, 1200, 700);
        }

        bool IsShown(FrameworkElement pane)
            => pane.Visibility == Visibility.Visible
               && Find<FrameworkElement>(inspector, "TreeWorkbenchPane").Visibility == Visibility.Visible;
    }

    private static void VerifyLabels(Button[] buttons, string language, params string[] labels)
    {
        InspectorLocalization.Default.SetLanguage(language);
        DrainDispatcher();
        for (var index = 0; index < buttons.Length; index++)
        {
            var button = buttons[index];
            var tooltip = button.ToolTip is TextBlock text ? text.Text : button.ToolTip as string;
            Require(AutomationProperties.GetName(button) == labels[index] && tooltip == labels[index],
                "Incorrect navigation automation name or tooltip for " + button.Name + " in " + language + ".");
        }
    }

    private static Visibility VisibleWhen(bool visible)
    {
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsWithin(DependencyObject element, DependencyObject container)
    {
        for (var parent = LogicalTreeHelper.GetParent(element); parent is not null; parent = LogicalTreeHelper.GetParent(parent))
        {
            if (ReferenceEquals(parent, container))
            {
                return true;
            }
        }

        return false;
    }

    private static void SetPaneVisible(MenuItem item, bool visible)
    {
        if (item.IsChecked != visible)
        {
            item.IsChecked = visible;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
            DrainDispatcher();
        }
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        DrainDispatcher();
    }

    private static void Arrange(DockPanel chrome, double width, double height)
    {
        WorkbenchLayoutSmoke.Arrange(chrome, new Rect(0, 0, width, height));
    }

    private static void DrainDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    private static T Find<T>(FrameworkElement owner, string name)
        where T : class
    {
        return owner.FindName(name) as T
            ?? throw new InvalidOperationException("Missing workbench navigation element " + name + ".");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
