namespace Snoop.Host.SettingsSmoke;

using System;
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

            var customPropertiesWidth = 0.0;
            WithInspector(untouched =>
            {
                VerifyLayout(untouched, DefaultItem, 282, 354, 210);
                WithInspector(edited =>
                {
                    VerifyLabels(edited, "en-US", "Workspace layout", "Default layout", "Preview focus", "Inspector focus", "Custom");
                    VerifyLabels(edited, "zh-CN", "工作台布局", "默认布局", "预览优先", "属性优先", "自定义");
                    customPropertiesWidth = VerifyPresetAfterCollapseAndResize(edited);
                });
                VerifySavedLayout(layoutPath, 210, customPropertiesWidth, 160);
            });
            VerifySavedLayout(layoutPath, 210, customPropertiesWidth, 160);
            var foldedDocument = XDocument.Load(layoutPath).Root!;
            Require((bool?)foldedDocument.Element("WorkbenchPropertiesCollapsed") == true,
                "Closing an older untouched workbench lost the other window's collapsed properties pane.");

            settings.Reload();
            WithInspector(reopened =>
            {
                VerifySelectedLayout(reopened, CustomItem);
                Require(!Find<MenuItem>(reopened, "ShowPropertiesPaneItem").IsChecked
                        && Find<TabControl>(reopened, "PropertiesWorkbenchPane").Visibility == Visibility.Collapsed
                        && Find<GridSplitter>(reopened, "PropertiesSplitter").Visibility == Visibility.Collapsed,
                    "Reopening a workbench did not keep the properties pane and its splitter folded.");
                SetPaneVisibility(reopened, "ShowPropertiesPaneItem", true);
                VerifyLayout(reopened, CustomItem, 210, customPropertiesWidth, 160);

                Click(Find<MenuItem>(reopened, PropertiesItem));
                VerifyLayout(reopened, PropertiesItem, 270, 460, 240);
            });
            VerifySavedLayout(layoutPath, 270, 460, 240);

            settings.Reload();
            WithInspector(reopened =>
            {
                VerifyLayout(reopened, PropertiesItem, 270, 460, 240);
                Click(Find<MenuItem>(reopened, DefaultItem));
                VerifyLayout(reopened, DefaultItem, 282, 354, 210);
            });

            new XDocument(new XElement("Settings",
                new XElement("WorkbenchDensity", CoreSettings.CompactDensity),
                new XElement("WorkbenchTreeFraction", "0.28"),
                new XElement("WorkbenchDiagnosticsFraction", "0.22"),
                new XElement("WorkbenchTreeWidth", "320"),
                new XElement("WorkbenchPropertiesWidth", "405"),
                new XElement("WorkbenchDiagnosticsHeight", "220"),
                new XElement("WorkbenchTreeCollapsed", true),
                new XElement("WorkbenchPropertiesCollapsed", true),
                new XElement("WorkbenchDiagnosticsCollapsed", true))).Save(layoutPath);
            settings.Reload();

            WithInspector(reopened =>
            {
                Require(!Find<MenuItem>(reopened, "ShowTreePaneItem").IsChecked
                        && !Find<MenuItem>(reopened, "ShowPropertiesPaneItem").IsChecked
                        && !Find<MenuItem>(reopened, "ShowDiagnosticsPaneItem").IsChecked,
                    "The saved folded panes were not restored.");
                Click(Find<MenuItem>(reopened, "ResetWorkbenchLayoutItem"));
                VerifyVisiblePanes(reopened);
                VerifyLayout(reopened, DefaultItem, 282, 354, 210);
                Require(settings.WorkbenchDensity == CoreSettings.CompactDensity, "Reset layout changed the density preference.");
            });
            var resetDocument = XDocument.Load(layoutPath).Root!;
            Require(resetDocument.Element("WorkbenchTreeFraction") == null
                    && resetDocument.Element("WorkbenchDiagnosticsFraction") == null
                    && resetDocument.Element("WorkbenchTreeWidth") == null
                    && resetDocument.Element("WorkbenchPropertiesWidth") == null
                    && resetDocument.Element("WorkbenchDiagnosticsHeight") == null,
                "Reset layout kept an old fraction or an explicit pane size.");
            Require((bool?)resetDocument.Element("WorkbenchTreeCollapsed") != true
                    && (bool?)resetDocument.Element("WorkbenchPropertiesCollapsed") != true
                    && (bool?)resetDocument.Element("WorkbenchDiagnosticsCollapsed") != true,
                "Reset layout retained a persisted fold.");
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

        Console.WriteLine("PASS: workbench layout presets, custom sizes and pane folds survive XML reload");
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
            VerifyLayout(inspector, PreviewItem, 210, 285, 160);
            Require(ReferenceEquals(inspector.CurrentSelection, selection) && selection.IsSelected
                    && ReferenceEquals(propertyInspector.RootTarget, target) && Equals(target.Content, "Selected value")
                    && properties.SelectedIndex == 1 && diagnostics.SelectedIndex == 1
                    && propertyInspector.FilterString == "Width",
                "A layout preset lost a tab, filter, selection or value.");

            var splitter = Find<GridSplitter>(inspector, "PropertiesSplitter");
            ((ITransformProvider)new GridSplitterAutomationPeer(splitter)).Move(-50, 0);
            WorkbenchLayoutSmoke.Arrange(inspector);
            var customPropertiesWidth = WorkbenchLayoutSmoke.ReadPaneSizes(inspector).Properties;
            Require(customPropertiesWidth > 285, "The right splitter did not widen the properties pane.");
            VerifyLayout(inspector, CustomItem, 210, customPropertiesWidth, 160);
            SetPaneVisibility(inspector, "ShowPropertiesPaneItem", false);
            VerifySelectedLayout(inspector, CustomItem);
            SetPaneVisibility(inspector, "ShowPropertiesPaneItem", true);
            VerifyLayout(inspector, CustomItem, 210, customPropertiesWidth, 160);
            SetPaneVisibility(inspector, "ShowPropertiesPaneItem", false);
            return customPropertiesWidth;
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

    private static void VerifyLayout(SnoopUI inspector, string expectedItem, double tree, double properties, double diagnostics)
    {
        VerifySelectedLayout(inspector, expectedItem);
        WorkbenchLayoutSmoke.Arrange(inspector);
        WorkbenchLayoutSmoke.VerifyPaneSizes(inspector, tree, properties, diagnostics);
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
            "Custom layout should be a visible indicator only when pane sizes differ from the presets.");
    }

    private static void VerifySavedLayout(string path, double tree, double properties, double diagnostics)
    {
        var xml = XDocument.Load(path).Root;
        WorkbenchLayoutSmoke.RequireSavedSize(xml, "WorkbenchTreeWidth", tree);
        WorkbenchLayoutSmoke.RequireSavedSize(xml, "WorkbenchPropertiesWidth", properties);
        WorkbenchLayoutSmoke.RequireSavedSize(xml, "WorkbenchDiagnosticsHeight", diagnostics);
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

    private static void WithInspector(Action<SnoopUI> action)
    {
        var inspector = new SnoopUI();
        // Hidden smoke-test windows do not run OnSourceInitialized.
        CacheManager.Instance.IncreaseUsageCount();
        try
        {
            WorkbenchLayoutSmoke.Arrange(inspector);
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
