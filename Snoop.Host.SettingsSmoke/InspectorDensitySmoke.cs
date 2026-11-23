namespace Snoop.Host.SettingsSmoke;

using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using Snoop.Controls;
using Snoop.Data.Tree;
using Snoop.Localization;
using Snoop.Views;
using Snoop.Windows;
using CoreSettings = Snoop.Core.Settings;

internal static class InspectorDensitySmoke
{
    public static void Verify(Application app, SnoopUI inspector, string settingsDirectory)
    {
        var menu = Find<MenuItem>(inspector, "WorkbenchDensityMenu");
        var comfortable = Find<MenuItem>(inspector, "ComfortableDensityItem");
        var compact = Find<MenuItem>(inspector, "CompactDensityItem");
        var localization = InspectorLocalization.Default;
        var originalDensity = CoreSettings.Default.WorkbenchDensity;
        var originalLanguage = localization.LanguageTag;
        var originalSelection = inspector.CurrentSelection;
        var resources = app.Resources;
        var resourceCount = resources.Count;
        var dictionaryCount = resources.MergedDictionaries.Count;
        var propertyInspector = Find<PropertyInspector>(inspector, "PropertyInspector");
        var propertyGrid = Find<PropertyGrid2>(propertyInspector, "PropertyGrid");
        var properties = Find<ListView>(propertyGrid, "ListView");
        var diagnosticsView = FindLogicalDescendant<DiagnosticsView>(inspector)
            ?? throw new InvalidOperationException("Missing workbench diagnostics view.");
        var diagnostics = Find<ListView>(diagnosticsView, "diagnostics");
        var tree = Find<ProperTreeView>(inspector, "Tree");
        var treeRow = new ProperTreeViewItem(new WeakReference(tree)) { Style = tree.ItemContainerStyle };
        var propertyRow = new ListViewItem { Style = properties.ItemContainerStyle };
        var diagnosticRow = new ListViewItem { Style = diagnostics.ItemContainerStyle };
        var target = new Button { Content = "Edited target value" };
        var marker = new object();
        target.Resources.Add("DensitySmokeResource", marker);
        using var treeService = TreeService.From(TreeType.Visual);
        using var selection = new TreeItem(target, null, treeService);
        inspector.TreeItems.Add(selection);

        try
        {
            inspector.CurrentSelection = selection;
            localization.SetLanguage("en-US");
            Click(comfortable);
            RequireLabel(menu, "Density");
            RequireLabel(comfortable, "Comfortable");
            RequireLabel(compact, "Compact");
            VerifyRows(CoreSettings.ComfortableDensity, new Thickness(1, 2, 0, 2), 30, 24);

            Click(compact);
            VerifyRows(CoreSettings.CompactDensity, new Thickness(1, 0, 0, 0), 24, 19);
            Click(compact);
            VerifyRows(CoreSettings.CompactDensity, new Thickness(1, 0, 0, 0), 24, 19);

            localization.SetLanguage("zh-CN");
            DrainDispatcher();
            RequireLabel(menu, "界面密度");
            RequireLabel(comfortable, "舒适");
            RequireLabel(compact, "紧凑");
            VerifyRows(CoreSettings.CompactDensity, new Thickness(1, 0, 0, 0), 24, 19);

            Click(comfortable);
            VerifyRows(CoreSettings.ComfortableDensity, new Thickness(1, 2, 0, 2), 30, 24);
            VerifyPersistence();
        }
        finally
        {
            inspector.CurrentSelection = originalSelection;
            inspector.TreeItems.Remove(selection);
            localization.SetLanguage(originalLanguage);
            CoreSettings.Default.WorkbenchDensity = originalDensity;
        }

        Console.WriteLine("PASS: workbench density switches live, preserves selection and target resources, and round-trips through settings");

        void VerifyRows(string expected, Thickness treePadding, double propertyHeight, double diagnosticHeight)
        {
            Require(string.Equals(CoreSettings.Default.WorkbenchDensity, expected, StringComparison.Ordinal),
                "Selected workbench density did not update to " + expected);
            Require(comfortable.IsChecked == (expected == CoreSettings.ComfortableDensity)
                    && compact.IsChecked == (expected == CoreSettings.CompactDensity),
                "The density menu did not keep exactly one choice checked.");
            Require(treeRow.Padding == treePadding, "Unexpected " + expected + " tree row padding: " + treeRow.Padding);
            Require(propertyRow.MinHeight == propertyHeight, "Unexpected " + expected + " property row height: " + propertyRow.MinHeight);
            Require(diagnosticRow.MinHeight == diagnosticHeight, "Unexpected " + expected + " diagnostic row height: " + diagnosticRow.MinHeight);
            Require(ReferenceEquals(inspector.CurrentSelection, selection) && ReferenceEquals(propertyInspector.RootTarget, target)
                    && Equals(target.Content, "Edited target value"),
                "Changing density replaced the selected target or its value.");
            Require(ReferenceEquals(app.Resources, resources) && resources.Count == resourceCount
                    && resources.MergedDictionaries.Count == dictionaryCount
                    && ReferenceEquals(target.Resources["DensitySmokeResource"], marker),
                "Changing density modified the target or application resources.");
        }

        void VerifyPersistence()
        {
            var file = Path.Combine(settingsDirectory, "WorkbenchDensitySmoke.xml");
            try
            {
                var settings = new CoreSettings { SettingsFile = file };
                File.WriteAllText(file, "<Settings><ShowDefaults>false</ShowDefaults></Settings>");
                settings.Load();
                Require(settings.WorkbenchDensity == CoreSettings.ComfortableDensity && !settings.ShowDefaults,
                    "Legacy workbench settings did not retain their values and default to Comfortable.");

                settings.WorkbenchDensity = CoreSettings.CompactDensity;
                settings.Save();
                Require((string?)XDocument.Load(file).Root?.Element("WorkbenchDensity") == "Compact",
                    "The Compact preference was not serialized.");
                settings.Reset();
                Require(settings.WorkbenchDensity == CoreSettings.ComfortableDensity, "Density Reset did not restore Comfortable.");
                settings.Reload();
                Require(settings.WorkbenchDensity == CoreSettings.CompactDensity, "The Compact preference did not reload.");

                File.WriteAllText(file, "<Settings><WorkbenchDensity>Unknown</WorkbenchDensity><ShowDefaults>false</ShowDefaults></Settings>");
                settings.Reload();
                Require(settings.WorkbenchDensity == CoreSettings.ComfortableDensity && !settings.ShowDefaults,
                    "An unrecognized density did not fall back without discarding other settings.");
            }
            finally
            {
                File.Delete(file);
            }
        }
    }

    private static T Find<T>(FrameworkElement parent, string name)
        where T : class
    {
        return parent.FindName(name) as T ?? throw new InvalidOperationException("Missing density smoke control " + name);
    }

    private static T? FindLogicalDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if ((child as T ?? FindLogicalDescendant<T>(child)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static void Click(MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
        DrainDispatcher();
    }

    private static void DrainDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    private static void RequireLabel(MenuItem item, string expected)
    {
        Require(string.Equals(item.Header as string, expected, StringComparison.Ordinal),
            "Density menu expected " + expected + ", got " + item.Header);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
