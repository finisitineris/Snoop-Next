namespace Snoop.Host.SettingsSmoke;

using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Xml.Linq;
using Snoop.Controls;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Windows;
using CoreSettings = Snoop.Core.Settings;

internal static class WorkbenchPaneSmoke
{
    public static void Verify(SnoopUI inspector, string settingsDirectory)
    {
        var menu = Find<MenuItem>(inspector, "WorkbenchViewMenu");
        var showTree = Find<MenuItem>(inspector, "ShowTreePaneItem");
        var showProperties = Find<MenuItem>(inspector, "ShowPropertiesPaneItem");
        var showDiagnostics = Find<MenuItem>(inspector, "ShowDiagnosticsPaneItem");
        var grid = Find<Grid>(inspector, "WorkbenchGrid");
        var center = Find<Grid>(inspector, "WorkbenchCenterGrid");
        var treePane = Find<Grid>(inspector, "TreeWorkbenchPane");
        var previewPane = Find<FrameworkElement>(inspector, "WorkbenchPreviewPane");
        var treeColumn = Find<ColumnDefinition>(inspector, "TreePaneColumn");
        var treeSplitterColumn = Find<ColumnDefinition>(inspector, "TreeSplitterColumn");
        var propertiesColumn = Find<ColumnDefinition>(inspector, "InspectorPaneColumn");
        var propertiesSplitterColumn = Find<ColumnDefinition>(inspector, "PropertiesSplitterColumn");
        var previewRow = Find<RowDefinition>(inspector, "PreviewPaneRow");
        var diagnosticsRow = Find<RowDefinition>(inspector, "DiagnosticsPaneRow");
        var diagnosticsSplitterRow = Find<RowDefinition>(inspector, "DiagnosticsSplitterRow");
        var tree = Find<ProperTreeView>(inspector, "Tree");
        var treeFilter = Find<ComboBox>(inspector, "filterComboBox");
        var refresh = Find<Button>(inspector, "RefreshTreeButton");
        var treeLabel = Find<Label>(inspector, "TreeHeaderLabel");
        var treeType = Find<ComboBox>(inspector, "TreeTypeComboBox");
        var properties = Find<TabControl>(inspector, "PropertiesWorkbenchPane");
        var diagnostics = Find<TabControl>(inspector, "DiagnosticsWorkbenchPane");
        var propertyInspector = Find<PropertyInspector>(inspector, "PropertyInspector");
        var treeSplitter = Find<GridSplitter>(inspector, "GridSplitter");
        var propertiesSplitter = Find<GridSplitter>(inspector, "PropertiesSplitter");
        var horizontalSplitter = Find<GridSplitter>(inspector, "DiagnosticsSplitter");
        var status = Find<StatusBar>(inspector, "WorkbenchStatusBar");
        var reset = Find<MenuItem>(inspector, "ResetWorkbenchLayoutItem");

        var savedLanguage = InspectorLocalization.Default.LanguageTag;
        var savedSelection = inspector.CurrentSelection;
        var savedPropertyTab = properties.SelectedIndex;
        var savedDiagnosticsTab = diagnostics.SelectedIndex;
        var savedFilter = propertyInspector.FilterString;
        var settings = CoreSettings.Default;
        var savedTreeFraction = settings.WorkbenchTreeFraction;
        var savedDiagnosticsFraction = settings.WorkbenchDiagnosticsFraction;
        var savedTreeWidth = settings.WorkbenchTreeWidth;
        var savedPropertiesWidth = settings.WorkbenchPropertiesWidth;
        var savedDiagnosticsHeight = settings.WorkbenchDiagnosticsHeight;
        var savedTreeCollapsed = settings.WorkbenchTreeCollapsed;
        var savedPropertiesCollapsed = settings.WorkbenchPropertiesCollapsed;
        var savedDiagnosticsCollapsed = settings.WorkbenchDiagnosticsCollapsed;
        var savedDensity = settings.WorkbenchDensity;
        var savedTheme = settings.ThemeMode;
        using var treeService = TreeService.From(TreeType.Visual);
        var target = new Button { Content = "Edited value" };
        using var selection = new TreeItem(target, null, treeService);
        inspector.TreeItems.Add(selection);

        try
        {
            Require(showTree.IsCheckable && showTree.IsChecked && showProperties.IsCheckable && showProperties.IsChecked
                    && showDiagnostics.IsCheckable && showDiagnostics.IsChecked,
                "The three workbench panes must start visible with checked View items.");
            InspectorLocalization.Default.SetLanguage("en-US");
            DrainDispatcher();
            Require(menu.Header as string == "_View", "Missing English View menu caption and access key.");
            InspectorLocalization.Default.SetLanguage("zh-CN");
            DrainDispatcher();
            Require(menu.Header as string == "视图(_V)", "Missing Chinese View menu caption and access key.");
            inspector.CurrentSelection = selection;
            propertyInspector.FilterString = "Width";
            properties.SelectedIndex = 1;
            diagnostics.SelectedIndex = 1;

            Arrange(grid);
            var originalTreeWidth = treeColumn.ActualWidth;
            var originalPropertiesWidth = propertiesColumn.ActualWidth;
            var originalDiagnosticsHeight = diagnosticsRow.ActualHeight;
            Move(treeSplitter, 42, 0);
            Arrange(grid);
            Move(propertiesSplitter, -37, 0);
            Arrange(grid);
            Move(horizontalSplitter, 0, -45);
            Arrange(grid);
            var treeWidth = treeColumn.ActualWidth;
            var propertiesWidth = propertiesColumn.ActualWidth;
            var diagnosticsHeight = diagnosticsRow.ActualHeight;
            Require(treeWidth > originalTreeWidth && propertiesWidth > originalPropertiesWidth
                    && diagnosticsHeight > originalDiagnosticsHeight,
                "Each native splitter must resize its own workbench pane.");

            for (var mask = 0; mask < 8; ++mask)
            {
                var treeShown = (mask & 1) != 0;
                var propertiesShown = (mask & 2) != 0;
                var diagnosticsShown = (mask & 4) != 0;
                SetChecked(showTree, treeShown);
                SetChecked(showProperties, propertiesShown);
                SetChecked(showDiagnostics, diagnosticsShown);
                Arrange(grid);

                Require(HasVisibility(treePane, treeShown) && treePane.Children.Contains(tree) && treePane.Children.Contains(treeFilter)
                        && HasVisibility(treeLabel, treeShown)
                        && HasVisibility(treeType, treeShown),
                    "The tree, its header and filter controls should collapse together.");
                Require(refresh.Visibility == Visibility.Visible && refresh.IsEnabled,
                    "The workbench refresh command must stay visible with any pane combination.");
                Require(HasVisibility(properties, propertiesShown) && HasVisibility(diagnostics, diagnosticsShown),
                    "The Properties and Diagnostics panes should collapse independently.");
                Require(HasVisibility(treeSplitter, treeShown) && HasVisibility(propertiesSplitter, propertiesShown)
                        && HasVisibility(horizontalSplitter, diagnosticsShown),
                    "Each splitter must depend on its own pane and the persistent center preview.");
                Require(menu.Visibility == Visibility.Visible && status.Visibility == Visibility.Visible && status.ActualWidth > 0,
                    "The View menu and status bar must remain available with any pane combination.");

                Require(treeShown ? tree.ActualWidth > 0 && tree.ActualHeight > 0
                            : treeColumn.ActualWidth == 0 && treeSplitterColumn.ActualWidth == 0,
                    "The tree should have a surface when shown and release its column when hidden.");
                Require(propertiesShown ? properties.ActualWidth > 0 && properties.ActualHeight > 0
                            : propertiesColumn.ActualWidth == 0 && propertiesSplitterColumn.ActualWidth == 0,
                    "Properties should have a surface when shown and release its column when hidden.");
                Require(diagnosticsShown ? diagnostics.ActualWidth > 0 && diagnostics.ActualHeight > 0
                            : diagnosticsRow.ActualHeight == 0 && diagnosticsSplitterRow.ActualHeight == 0,
                    "Diagnostics should have a surface when shown and release its row when hidden.");
                Require(center.Children.Contains(previewPane) && center.Children.Contains(diagnostics)
                        && Grid.GetRow(diagnostics) > Grid.GetRow(previewPane)
                        && previewPane.Visibility == Visibility.Visible
                        && previewPane.ActualWidth > 0 && previewPane.ActualHeight > 0,
                    "The center preview must remain available above Diagnostics with every pane combination.");
                if (!diagnosticsShown)
                {
                    RequireClose(previewPane.ActualHeight, center.ActualHeight, "Preview did not fill the center with Diagnostics folded.");
                }

                Require(ReferenceEquals(inspector.CurrentSelection, selection) && selection.IsSelected
                        && ReferenceEquals(propertyInspector.RootTarget, target) && Equals(target.Content, "Edited value")
                        && propertyInspector.FilterString == "Width"
                        && properties.SelectedIndex == 1 && diagnostics.SelectedIndex == 1,
                    "Collapsing a pane lost its tab, property filter, selection or value.");
            }

            RequireClose(treeColumn.ActualWidth, treeWidth, "Restoring the tree lost its last splitter size.");
            RequireClose(propertiesColumn.ActualWidth, propertiesWidth, "Restoring Properties lost its last splitter size.");
            RequireClose(diagnosticsRow.ActualHeight, diagnosticsHeight, "Restoring Diagnostics lost its last splitter size.");
            Require(treeColumn.ActualWidth >= 164 && propertiesColumn.ActualWidth >= 239 && center.ActualWidth >= 239
                    && previewRow.ActualHeight >= 109 && diagnosticsRow.ActualHeight >= 109,
                "The expanded tree, Properties, Preview or Diagnostics lost its normal minimum size.");

            SetChecked(showTree, false);
            SetChecked(showProperties, false);
            SetChecked(showDiagnostics, false);
            reset.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, reset));
            DrainDispatcher();
            Arrange(grid);
            Require(showTree.IsChecked && showProperties.IsChecked && showDiagnostics.IsChecked,
                "Reset layout should show all three panes.");
            RequireClose(treeColumn.ActualWidth, 282, "Reset layout did not restore the default tree width.");
            RequireClose(propertiesColumn.ActualWidth, 354, "Reset layout did not restore the default Properties width.");
            RequireClose(diagnosticsRow.ActualHeight, 210, "Reset layout did not restore the default Diagnostics height.");
            Require(settings.WorkbenchDensity == savedDensity && settings.ThemeMode == savedTheme
                    && ReferenceEquals(inspector.CurrentSelection, selection),
                "Reset layout changed the density, theme or selection.");

            VerifySaveWhileCollapsed(settingsDirectory);
        }
        finally
        {
            inspector.CurrentSelection = savedSelection;
            inspector.TreeItems.Remove(selection);
            properties.SelectedIndex = savedPropertyTab;
            diagnostics.SelectedIndex = savedDiagnosticsTab;
            propertyInspector.FilterString = savedFilter;
            InspectorLocalization.Default.SetLanguage(savedLanguage);
            settings.WorkbenchTreeFraction = savedTreeFraction;
            settings.WorkbenchDiagnosticsFraction = savedDiagnosticsFraction;
            settings.WorkbenchTreeWidth = savedTreeWidth;
            settings.WorkbenchPropertiesWidth = savedPropertiesWidth;
            settings.WorkbenchDiagnosticsHeight = savedDiagnosticsHeight;
            settings.WorkbenchTreeCollapsed = savedTreeCollapsed;
            settings.WorkbenchPropertiesCollapsed = savedPropertiesCollapsed;
            settings.WorkbenchDiagnosticsCollapsed = savedDiagnosticsCollapsed;
        }

        Console.WriteLine("PASS: workbench panes collapse and restore without losing state or splitter sizes");
    }

    private static void VerifySaveWhileCollapsed(string settingsDirectory)
    {
        var settings = CoreSettings.Default;
        var originalFile = settings.SettingsFile;
        var originalPlacement = settings.SnoopUIWindowPlacement;
        var originalShowDefaults = settings.ShowDefaults;
        var originalShowPreviewer = settings.ShowPreviewer;
        var temporaryFile = Path.Combine(settingsDirectory, "WorkbenchPaneSmoke.xml");
        settings.SettingsFile = temporaryFile;
        try
        {
            var inspector = new SnoopUI();
            // This hidden fixture window does not run OnSourceInitialized.
            CacheManager.Instance.IncreaseUsageCount();
            double treeWidth;
            double propertiesWidth;
            double diagnosticsHeight;
            try
            {
                var grid = Find<Grid>(inspector, "WorkbenchGrid");
                Arrange(grid);
                Move(Find<GridSplitter>(inspector, "GridSplitter"), 42, 0);
                Arrange(grid);
                Move(Find<GridSplitter>(inspector, "PropertiesSplitter"), -37, 0);
                Arrange(grid);
                Move(Find<GridSplitter>(inspector, "DiagnosticsSplitter"), 0, -45);
                Arrange(grid);
                treeWidth = Find<ColumnDefinition>(inspector, "TreePaneColumn").ActualWidth;
                propertiesWidth = Find<ColumnDefinition>(inspector, "InspectorPaneColumn").ActualWidth;
                diagnosticsHeight = Find<RowDefinition>(inspector, "DiagnosticsPaneRow").ActualHeight;
                SetChecked(Find<MenuItem>(inspector, "ShowTreePaneItem"), false);
                SetChecked(Find<MenuItem>(inspector, "ShowPropertiesPaneItem"), false);
                SetChecked(Find<MenuItem>(inspector, "ShowDiagnosticsPaneItem"), false);
            }
            finally
            {
                inspector.Close();
            }

            var xml = XDocument.Load(temporaryFile).Root;
            RequireSavedSize(xml, "WorkbenchTreeWidth", treeWidth);
            RequireSavedSize(xml, "WorkbenchPropertiesWidth", propertiesWidth);
            RequireSavedSize(xml, "WorkbenchDiagnosticsHeight", diagnosticsHeight);
            Require((bool?)xml?.Element("WorkbenchTreeCollapsed") is true
                    && (bool?)xml?.Element("WorkbenchPropertiesCollapsed") is true
                    && (bool?)xml?.Element("WorkbenchDiagnosticsCollapsed") is true,
                "Closing a folded workbench did not save its three pane states.");
        }
        finally
        {
            settings.SettingsFile = originalFile;
            settings.SnoopUIWindowPlacement = originalPlacement;
            settings.ShowDefaults = originalShowDefaults;
            settings.ShowPreviewer = originalShowPreviewer;
            File.Delete(temporaryFile);
        }
    }

    private static void RequireSavedSize(XElement? xml, string name, double expected)
    {
        Require(double.TryParse((string?)xml?.Element(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var size),
            "Closing while collapsed failed to write " + name + ".");
        RequireClose(size, expected, "Closing while collapsed lost " + name + ".");
    }

    private static void SetChecked(MenuItem item, bool isChecked)
    {
        if (item.IsChecked != isChecked)
        {
            // A native checkable MenuItem toggles its state before raising Click.
            item.IsChecked = isChecked;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
            DrainDispatcher();
        }
    }

    private static bool HasVisibility(UIElement element, bool shown)
    {
        return element.Visibility == (shown ? Visibility.Visible : Visibility.Collapsed);
    }

    private static void Arrange(Grid grid)
    {
        WorkbenchLayoutSmoke.Arrange(grid, new Rect(0, 0, 960, 540));
    }

    private static void Move(GridSplitter splitter, double x, double y)
    {
        ((ITransformProvider)new GridSplitterAutomationPeer(splitter)).Move(x, y);
    }

    private static void RequireClose(double actual, double expected, string message)
    {
        Require(Math.Abs(actual - expected) < 1, message);
    }

    private static void DrainDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static T Find<T>(FrameworkElement owner, string name)
        where T : class
    {
        return owner.FindName(name) as T
            ?? throw new InvalidOperationException("Missing workbench pane element " + name + ".");
    }
}
