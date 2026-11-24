namespace Snoop.Host.SettingsSmoke;

using System;
using System.Windows;
using System.Windows.Automation;
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
        var previewTab = Find<TabItem>(inspector, "WorkbenchPreviewTab");
        var treeButton = Find<Button>(inspector, "NavigateTreeButton");
        var propertiesButton = Find<Button>(inspector, "NavigatePropertiesButton");
        var diagnosticsButton = Find<Button>(inspector, "NavigateDiagnosticsButton");
        var eventsButton = Find<Button>(inspector, "NavigateEventsButton");
        var previewButton = Find<Button>(inspector, "NavigatePreviewButton");
        var buttons = new[] { treeButton, propertiesButton, diagnosticsButton, eventsButton, previewButton };
        var focusVisual = inspector.FindResource("SnoopNext.WorkbenchFocusVisual");
        var chrome = Find<DockPanel>(inspector, "WorkbenchChrome");
        chrome.Measure(new Size(480, 320));
        chrome.Arrange(new Rect(0, 0, 480, 320));
        DrainDispatcher();
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
        var savedTreeWidth = grid.ColumnDefinitions[0].Width;
        var savedInspectorWidth = grid.ColumnDefinitions[1].Width;
        var savedPropertiesHeight = grid.RowDefinitions[1].Height;
        var savedDiagnosticsHeight = grid.RowDefinitions[2].Height;
        var savedLanguage = InspectorLocalization.Default.LanguageTag;
        var savedSelection = inspector.CurrentSelection;
        var savedPropertiesTab = properties.SelectedIndex;
        var savedDiagnosticsTab = diagnostics.SelectedIndex;
        var propertyInspector = Find<PropertyInspector>(inspector, "PropertyInspector");
        var savedFilter = propertyInspector.FilterString;
        var preview = Find<Previewer>(inspector, "PreviewArea");
        var previewActive = preview.IsActive;
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
            diagnostics.SelectedItem = previewTab;
            grid.ColumnDefinitions[0].Width = new GridLength(0.42, GridUnitType.Star);
            grid.ColumnDefinitions[1].Width = new GridLength(0.58, GridUnitType.Star);
            grid.RowDefinitions[1].Height = new GridLength(0.64, GridUnitType.Star);
            grid.RowDefinitions[2].Height = new GridLength(0.36, GridUnitType.Star);

            SetPaneVisible(showTree, false);
            SetPaneVisible(showProperties, false);
            SetPaneVisible(showDiagnostics, false);
            Require(rail.Visibility == Visibility.Visible && treeButton.IsEnabled,
                "The navigation rail should remain usable with every pane hidden.");

            Click(treeButton);
            VerifyPanes(true, false, false);
            Require(ReferenceEquals(properties.SelectedItem, dataContextTab) && ReferenceEquals(diagnostics.SelectedItem, previewTab),
                "Opening the element tree changed a tab.");

            SetPaneVisible(showTree, false);
            Click(propertiesButton);
            VerifyPanes(false, true, false);
            Require(ReferenceEquals(properties.SelectedItem, propertiesTab) && ReferenceEquals(diagnostics.SelectedItem, previewTab),
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
            VerifyPanes(true, true, true);
            Require(ReferenceEquals(properties.SelectedItem, dataContextTab) && ReferenceEquals(diagnostics.SelectedItem, previewTab),
                "Preview navigation did not select only the Preview tab.");
            Require(ReferenceEquals(inspector.CurrentSelection, selection) && selection.IsSelected
                    && ReferenceEquals(propertyInspector.RootTarget, target) && Equals(target.Content, "Edited value")
                    && propertyInspector.FilterString == "Width" && preview.IsActive == previewActive,
                "Navigation lost the selected element, its value, the property filter or the preview preference.");
            RequireRatio(grid.ColumnDefinitions[0].Width, grid.ColumnDefinitions[1].Width, 0.42);
            RequireRatio(grid.RowDefinitions[2].Height, grid.RowDefinitions[1].Height, 0.36);
        }
        finally
        {
            SetPaneVisible(showTree, true);
            SetPaneVisible(showProperties, true);
            SetPaneVisible(showDiagnostics, true);
            grid.ColumnDefinitions[0].Width = savedTreeWidth;
            grid.ColumnDefinitions[1].Width = savedInspectorWidth;
            grid.RowDefinitions[1].Height = savedPropertiesHeight;
            grid.RowDefinitions[2].Height = savedDiagnosticsHeight;
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
            Require(showTree.IsChecked == treeVisible && tree.Visibility == VisibleWhen(treeVisible)
                    && showProperties.IsChecked == propertiesVisible && properties.Visibility == VisibleWhen(propertiesVisible)
                    && showDiagnostics.IsChecked == diagnosticsVisible && diagnostics.Visibility == VisibleWhen(diagnosticsVisible),
                "The View menu and workbench pane visibility should stay in sync.");
        }
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

    private static void RequireRatio(GridLength first, GridLength second, double expected)
    {
        Require(first.IsStar && second.IsStar
                && Math.Abs((first.Value / (first.Value + second.Value)) - expected) < 0.000001,
            "Navigation lost a cached splitter ratio.");
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
