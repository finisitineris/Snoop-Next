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
        var tree = Find<ProperTreeView>(inspector, "Tree");
        var treeFilter = Find<ComboBox>(inspector, "filterComboBox");
        var refresh = Find<Button>(inspector, "RefreshTreeButton");
        var treeLabel = Find<Label>(inspector, "TreeHeaderLabel");
        var treeType = Find<ComboBox>(inspector, "TreeTypeComboBox");
        var properties = Find<TabControl>(inspector, "PropertiesWorkbenchPane");
        var diagnostics = Find<TabControl>(inspector, "DiagnosticsWorkbenchPane");
        var propertyInspector = Find<PropertyInspector>(inspector, "PropertyInspector");
        var verticalSplitter = Find<GridSplitter>(inspector, "GridSplitter");
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
            Move(verticalSplitter, 75, 0);
            Arrange(grid);
            Move(horizontalSplitter, 0, -45);
            var treeFraction = TreeFraction(grid);
            var diagnosticsFraction = DiagnosticsFraction(grid);

            for (var mask = 0; mask < 8; ++mask)
            {
                var treeShown = (mask & 1) != 0;
                var propertiesShown = (mask & 2) != 0;
                var diagnosticsShown = (mask & 4) != 0;
                SetChecked(showTree, treeShown);
                SetChecked(showProperties, propertiesShown);
                SetChecked(showDiagnostics, diagnosticsShown);
                Arrange(grid);

                Require(HasVisibility(tree, treeShown) && HasVisibility(treeFilter, treeShown)
                        && HasVisibility(treeLabel, treeShown)
                        && HasVisibility(treeType, treeShown),
                    "The tree, its header and filter controls should collapse together.");
                Require(refresh.Visibility == Visibility.Visible && refresh.IsEnabled,
                    "The workbench refresh command must stay visible with any pane combination.");
                Require(HasVisibility(properties, propertiesShown) && HasVisibility(diagnostics, diagnosticsShown),
                    "The Properties and Diagnostics panes should collapse independently.");
                Require(HasVisibility(verticalSplitter, treeShown && (propertiesShown || diagnosticsShown))
                        && HasVisibility(horizontalSplitter, propertiesShown && diagnosticsShown),
                    "A splitter should be available only when both of its neighboring panes are shown.");
                Require(menu.Visibility == Visibility.Visible && status.Visibility == Visibility.Visible && status.ActualWidth > 0,
                    "The View menu and status bar must remain available with any pane combination.");

                Require(treeShown ? tree.ActualWidth > 0 && tree.ActualHeight > 0 : grid.ColumnDefinitions[0].ActualWidth == 0,
                    "The tree should have a surface when shown and release its column when hidden.");
                Require(!propertiesShown || (properties.ActualWidth > 0 && properties.ActualHeight > 0),
                    "The Properties pane should have a surface when shown.");
                Require(!diagnosticsShown || (diagnostics.ActualWidth > 0 && diagnostics.ActualHeight > 0),
                    "The Diagnostics pane should have a surface when shown.");
                Require(!diagnosticsShown || (Grid.GetRow(diagnostics) == (propertiesShown ? 2 : 0)
                                                && Grid.GetRowSpan(diagnostics) == (propertiesShown ? 1 : 3)),
                    "The Diagnostics pane should fill its column when Properties is hidden.");
                Require(ReferenceEquals(inspector.CurrentSelection, selection) && selection.IsSelected
                        && ReferenceEquals(propertyInspector.RootTarget, target) && Equals(target.Content, "Edited value")
                        && propertyInspector.FilterString == "Width"
                        && properties.SelectedIndex == 1 && diagnostics.SelectedIndex == 1,
                    "Collapsing a pane lost its tab, property filter, selection or value.");
            }

            RequireClose(TreeFraction(grid), treeFraction, "Restoring the tree lost its last splitter ratio.");
            RequireClose(DiagnosticsFraction(grid), diagnosticsFraction, "Restoring Diagnostics lost its last splitter ratio.");
            Require(grid.ColumnDefinitions[0].MinWidth >= 120 && grid.ColumnDefinitions[1].MinWidth >= 220
                    && grid.RowDefinitions[1].MinHeight >= 80 && grid.RowDefinitions[2].MinHeight >= 100,
                "The expanded pane minimum dimensions were lost.");

            SetChecked(showTree, false);
            SetChecked(showDiagnostics, false);
            reset.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, reset));
            DrainDispatcher();
            Require(showTree.IsChecked && showProperties.IsChecked && showDiagnostics.IsChecked,
                "Reset layout should show all three panes.");
            RequireClose(TreeFraction(grid), 1.0 / 3, "Reset layout did not restore the tree default ratio.");
            RequireClose(DiagnosticsFraction(grid), 3.0 / 13, "Reset layout did not restore the Diagnostics default ratio.");
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
        }

        Console.WriteLine("PASS: workbench panes collapse and restore without losing state or splitter ratios");
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
            double treeFraction;
            double diagnosticsFraction;
            try
            {
                var grid = Find<Grid>(inspector, "WorkbenchGrid");
                Arrange(grid);
                Move(Find<GridSplitter>(inspector, "GridSplitter"), 75, 0);
                Arrange(grid);
                Move(Find<GridSplitter>(inspector, "DiagnosticsSplitter"), 0, -45);
                treeFraction = TreeFraction(grid);
                diagnosticsFraction = DiagnosticsFraction(grid);
                SetChecked(Find<MenuItem>(inspector, "ShowTreePaneItem"), false);
                SetChecked(Find<MenuItem>(inspector, "ShowDiagnosticsPaneItem"), false);
            }
            finally
            {
                inspector.Close();
            }

            var xml = XDocument.Load(temporaryFile).Root;
            RequireSavedFraction(xml, "WorkbenchTreeFraction", treeFraction);
            RequireSavedFraction(xml, "WorkbenchDiagnosticsFraction", diagnosticsFraction);
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

    private static void RequireSavedFraction(XElement? xml, string name, double expected)
    {
        Require(double.TryParse((string?)xml?.Element(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction),
            "Closing while collapsed failed to write " + name + ".");
        RequireClose(fraction, expected, "Closing while collapsed lost " + name + ".");
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
        grid.Measure(new Size(960, 540));
        grid.Arrange(new Rect(0, 0, 960, 540));
    }

    private static void Move(GridSplitter splitter, double x, double y)
    {
        ((ITransformProvider)new GridSplitterAutomationPeer(splitter)).Move(x, y);
    }

    private static double TreeFraction(Grid grid)
    {
        return grid.ColumnDefinitions[0].Width.Value / (grid.ColumnDefinitions[0].Width.Value + grid.ColumnDefinitions[1].Width.Value);
    }

    private static double DiagnosticsFraction(Grid grid)
    {
        return grid.RowDefinitions[2].Height.Value / (grid.RowDefinitions[1].Height.Value + grid.RowDefinitions[2].Height.Value);
    }

    private static void RequireClose(double actual, double expected, string message)
    {
        Require(Math.Abs(actual - expected) < 0.000001, message);
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
