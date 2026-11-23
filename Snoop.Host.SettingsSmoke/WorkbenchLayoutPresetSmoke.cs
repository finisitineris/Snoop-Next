namespace Snoop.Host.SettingsSmoke;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using Snoop.Controls;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Windows;
using CoreSettings = Snoop.Core.Settings;

internal static class WorkbenchLayoutPresetSmoke
{
    private const double DefaultTreeFraction = 1.0 / 3;
    private const double DefaultDiagnosticsFraction = 3.0 / 13;
    private const double PreviewTreeFraction = 1.0 / 4;
    private const double PreviewDiagnosticsFraction = 1.0 / 2;
    private const double PropertiesTreeFraction = 3.0 / 10;
    private const double PropertiesDiagnosticsFraction = 1.0 / 6;
    private const string DefaultItem = "DefaultWorkbenchLayoutItem";
    private const string PreviewItem = "PreviewWorkbenchLayoutItem";
    private const string PropertiesItem = "PropertiesWorkbenchLayoutItem";
    private const string CustomItem = "CustomWorkbenchLayoutItem";
    private const string LayoutMenu = "WorkbenchLayoutMenu";

    public static void Verify(SnoopUI inspector, string settingsDirectory)
    {
        Find<MenuItem>(inspector, LayoutMenu);
        var settings = CoreSettings.Default;
        var originalFile = settings.SettingsFile;
        var originalLanguage = InspectorLocalization.Default.LanguageTag;
        var snapshotPath = Path.Combine(settingsDirectory, "WorkbenchLayoutPresetOriginal.xml");
        var layoutPath = Path.Combine(settingsDirectory, "WorkbenchLayoutPresetSmoke.xml");
        try
        {
            settings.SettingsFile = snapshotPath;
            settings.Save();
            settings.SettingsFile = layoutPath;
            new XDocument(new XElement("Settings",
                new XElement("WorkbenchDensity", CoreSettings.CompactDensity))).Save(layoutPath);
            settings.Reload();

            var customTreeFraction = 0.0;
            WithInspector(untouched =>
            {
                VerifyLayout(untouched, DefaultItem, DefaultTreeFraction, DefaultDiagnosticsFraction);
                WithInspector(edited =>
                {
                    VerifyLabels(edited, "en-US", "Workspace layout", "Default layout", "Preview focus", "Inspector focus", "Custom");
                    VerifyLabels(edited, "zh-CN", "工作台布局", "默认布局", "预览优先", "属性优先", "自定义");
                    customTreeFraction = VerifyPresetAfterCollapseAndResize(edited);
                });
                VerifySavedLayout(layoutPath, customTreeFraction, PreviewDiagnosticsFraction);
            });
            VerifySavedLayout(layoutPath, customTreeFraction, PreviewDiagnosticsFraction);

            settings.Reload();
            WithInspector(reopened =>
            {
                VerifyLayout(reopened, CustomItem, customTreeFraction, PreviewDiagnosticsFraction);
                Click(Find<MenuItem>(reopened, PropertiesItem));
                VerifyLayout(reopened, PropertiesItem, PropertiesTreeFraction, PropertiesDiagnosticsFraction);
            });
            VerifySavedLayout(layoutPath, PropertiesTreeFraction, PropertiesDiagnosticsFraction);

            settings.Reload();
            WithInspector(reopened =>
            {
                VerifyLayout(reopened, PropertiesItem, PropertiesTreeFraction, PropertiesDiagnosticsFraction);
                Click(Find<MenuItem>(reopened, DefaultItem));
                VerifyLayout(reopened, DefaultItem, DefaultTreeFraction, DefaultDiagnosticsFraction);

                Click(Find<MenuItem>(reopened, PreviewItem));
                SetPaneVisibility(reopened, "ShowDiagnosticsPaneItem", false);
                Click(Find<MenuItem>(reopened, "ResetWorkbenchLayoutItem"));
                VerifyVisiblePanes(reopened);
                VerifyLayout(reopened, DefaultItem, DefaultTreeFraction, DefaultDiagnosticsFraction);
                Require(settings.WorkbenchDensity == CoreSettings.CompactDensity, "Reset layout changed the density preference.");
            });
            var resetDocument = XDocument.Load(layoutPath).Root!;
            Require(resetDocument.Element("WorkbenchTreeFraction") == null
                    && resetDocument.Element("WorkbenchDiagnosticsFraction") == null,
                "The reset wrote explicit ratios instead of using the default layout.");
        }
        finally
        {
            if (File.Exists(snapshotPath))
            {
                settings.SettingsFile = snapshotPath;
                settings.Reload();
            }

            settings.SettingsFile = originalFile;
            InspectorLocalization.Default.SetLanguage(originalLanguage);
            File.Delete(snapshotPath);
            File.Delete(layoutPath);
        }

        Console.WriteLine("PASS: workbench layout presets and custom ratios survive XML reload");
    }

    private static double VerifyPresetAfterCollapseAndResize(SnoopUI inspector)
    {
        using var treeService = TreeService.From(TreeType.Visual);
        var target = new Button { Content = "Selected value" };
        using var selection = new TreeItem(target, null, treeService);
        inspector.TreeItems.Add(selection);
        inspector.CurrentSelection = selection;
        var properties = Find<TabControl>(inspector, "PropertiesWorkbenchPane");
        var diagnostics = Find<TabControl>(inspector, "DiagnosticsWorkbenchPane");
        var propertyInspector = Find<PropertyInspector>(inspector, "PropertyInspector");
        properties.SelectedIndex = 1;
        diagnostics.SelectedIndex = 1;
        propertyInspector.FilterString = "Width";
        try
        {
            SetPaneVisibility(inspector, "ShowTreePaneItem", false);
            SetPaneVisibility(inspector, "ShowPropertiesPaneItem", false);
            SetPaneVisibility(inspector, "ShowDiagnosticsPaneItem", false);
            Click(Find<MenuItem>(inspector, PreviewItem));
            VerifyVisiblePanes(inspector);
            VerifyLayout(inspector, PreviewItem, PreviewTreeFraction, PreviewDiagnosticsFraction);
            Require(ReferenceEquals(inspector.CurrentSelection, selection) && selection.IsSelected
                    && ReferenceEquals(propertyInspector.RootTarget, target) && Equals(target.Content, "Selected value")
                    && properties.SelectedIndex == 1 && diagnostics.SelectedIndex == 1
                    && propertyInspector.FilterString == "Width",
                "A layout preset lost a tab, filter, selection or value.");

            var grid = Find<Grid>(inspector, "WorkbenchGrid");
            grid.Measure(new Size(1200, 700));
            grid.Arrange(new Rect(0, 0, 1200, 700));
            var splitter = Find<GridSplitter>(inspector, "GridSplitter");
            ((ITransformProvider)new GridSplitterAutomationPeer(splitter)).Move(75, 0);
            var customTreeFraction = GetFraction(grid.ColumnDefinitions[0].Width, grid.ColumnDefinitions[1].Width);
            Require(customTreeFraction > PreviewTreeFraction, "The splitter did not change the preview layout's tree ratio.");
            VerifyLayout(inspector, CustomItem, customTreeFraction, PreviewDiagnosticsFraction);
            SetPaneVisibility(inspector, "ShowTreePaneItem", false);
            VerifySelectedLayout(inspector, CustomItem);
            SetPaneVisibility(inspector, "ShowTreePaneItem", true);
            VerifyLayout(inspector, CustomItem, customTreeFraction, PreviewDiagnosticsFraction);
            SetPaneVisibility(inspector, "ShowTreePaneItem", false);
            return customTreeFraction;
        }
        finally
        {
            inspector.CurrentSelection = null;
            inspector.TreeItems.Remove(selection);
        }
    }

    private static void VerifyLabels(SnoopUI inspector, string language, params string[] captions)
    {
        InspectorLocalization.Default.SetLanguage(language);
        DrainDispatcher();
        var names = new[] { LayoutMenu, DefaultItem, PreviewItem, PropertiesItem, CustomItem };
        for (var i = 0; i < names.Length; ++i)
        {
            Require(Find<MenuItem>(inspector, names[i]).Header as string == captions[i],
                "Incorrect layout preset caption for " + names[i] + " in " + language + ".");
        }
    }

    private static void VerifyVisiblePanes(SnoopUI inspector)
    {
        Require(Find<MenuItem>(inspector, "ShowTreePaneItem").IsChecked
                && Find<MenuItem>(inspector, "ShowPropertiesPaneItem").IsChecked
                && Find<MenuItem>(inspector, "ShowDiagnosticsPaneItem").IsChecked
                && Find<UIElement>(inspector, "Tree").Visibility == Visibility.Visible
                && Find<TabControl>(inspector, "PropertiesWorkbenchPane").Visibility == Visibility.Visible
                && Find<TabControl>(inspector, "DiagnosticsWorkbenchPane").Visibility == Visibility.Visible,
            "Applying a preset did not restore all workbench panes.");
    }

    private static void VerifyLayout(SnoopUI inspector, string expectedItem, double tree, double diagnostics)
    {
        VerifySelectedLayout(inspector, expectedItem);
        var grid = Find<Grid>(inspector, "WorkbenchGrid");
        RequireClose(GetFraction(grid.ColumnDefinitions[0].Width, grid.ColumnDefinitions[1].Width), tree, "Tree ratio");
        RequireClose(GetFraction(grid.RowDefinitions[2].Height, grid.RowDefinitions[1].Height), diagnostics, "Diagnostics ratio");
    }

    private static void VerifySelectedLayout(SnoopUI inspector, string expectedItem)
    {
        var menu = Find<MenuItem>(inspector, LayoutMenu);
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, menu));
        DrainDispatcher();
        var options = new[] { DefaultItem, PreviewItem, PropertiesItem, CustomItem };
        Require(options.All(name => Find<MenuItem>(inspector, name).IsChecked == (name == expectedItem)),
            "Only the current workbench layout should display a check mark.");
        var custom = Find<MenuItem>(inspector, CustomItem);
        Require(!custom.IsEnabled
                && custom.Visibility == (expectedItem == CustomItem ? Visibility.Visible : Visibility.Collapsed),
            "Custom layout should be a visible indicator only when the ratios differ from the presets.");
    }

    private static void VerifySavedLayout(string path, double tree, double diagnostics)
    {
        var xml = XDocument.Load(path).Root;
        RequireSavedFraction(xml, "WorkbenchTreeFraction", tree);
        RequireSavedFraction(xml, "WorkbenchDiagnosticsFraction", diagnostics);
    }

    private static void RequireSavedFraction(XElement? xml, string name, double expected)
    {
        Require(double.TryParse((string?)xml?.Element(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction),
            "Closing the workbench failed to save " + name + ".");
        RequireClose(fraction, expected, name);
    }

    private static void SetPaneVisibility(SnoopUI inspector, string name, bool shown)
    {
        var item = Find<MenuItem>(inspector, name);
        // Native checkable menu items update their state before raising Click.
        item.IsChecked = shown;
        Click(item);
    }

    private static void Click(MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
        DrainDispatcher();
    }

    private static double GetFraction(GridLength pane, GridLength other)
    {
        Require(pane.IsStar && other.IsStar, "The expanded panes must remain proportional.");
        return pane.Value / (pane.Value + other.Value);
    }

    private static void WithInspector(Action<SnoopUI> action)
    {
        var inspector = new SnoopUI();
        // Hidden smoke-test windows do not run OnSourceInitialized.
        CacheManager.Instance.IncreaseUsageCount();
        try
        {
            action(inspector);
        }
        finally
        {
            inspector.Close();
        }
    }

    private static T Find<T>(SnoopUI inspector, string name)
        where T : class
    {
        return inspector.FindName(name) as T
            ?? throw new InvalidOperationException("Missing workbench layout preset element " + name + ".");
    }

    private static void RequireClose(double actual, double expected, string state)
    {
        Require(Math.Abs(actual - expected) < 0.000001, state + " did not match the selected workbench layout.");
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
}
