namespace Snoop.Host.SettingsSmoke;

using System;
using System.Reflection;
using System.Windows.Controls;
using Snoop.Data.Tree;
using Snoop.Localization;
using Snoop.Windows;

internal static class InspectorFilterSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var filterBox = inspector.FindName("filterComboBox") as ComboBox
            ?? throw new InvalidOperationException("SnoopUI tree filter was not found");
        var processFilter = typeof(SnoopUI).GetMethod("ProcessFilter", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SnoopUI filter callback was not found");

        var root = new SmokeTreeItem(new object(), null, inspector.TreeService);
        var ordinaryChild = new SmokeTreeItem("ordinary", root, inspector.TreeService);
        var bindingErrorChild = new SmokeTreeItem(new object(), root, inspector.TreeService, hasBindingError: true);
        var systemResources = new SystemResourcesTreeItem(null, inspector.TreeService);
        SetRoot(inspector, nameof(SnoopUI.RootTreeItem), root);
        SetRoot(inspector, nameof(SnoopUI.SystemResourcesTreeItem), systemResources);

        var originalLanguage = InspectorLocalization.Default.LanguageTag;
        try
        {
            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                InspectorLocalization.Default.SetLanguage(language);

                filterBox.SetCurrentValue(ComboBox.TextProperty, "String");
                processFilter.Invoke(inspector, null);
                Require(filterBox.SelectedItem is null && inspector.TreeItems.Count == 1
                    && inspector.TreeItems[0] == ordinaryChild,
                    "literal tree search did not return the expected item in " + language);

                filterBox.SelectedIndex = 1;
                processFilter.Invoke(inspector, null);
                Require(inspector.Filter == InspectorLocalization.Default["Tooltip.ShowBindingErrors"]
                    && inspector.TreeItems.Count == 1 && inspector.TreeItems[0] == bindingErrorChild,
                    "binding-error action did not return the expected item in " + language);

                filterBox.SelectedIndex = 0;
                processFilter.Invoke(inspector, null);
                Require(inspector.Filter == string.Empty && filterBox.SelectedItem is null,
                    "clear action did not reset the tree filter in " + language);

                processFilter.Invoke(inspector, null);
                Require(inspector.TreeItems.Count == 2 && inspector.TreeItems[0] == systemResources
                    && inspector.TreeItems[1] == root,
                    "clear action did not restore the unfiltered tree in " + language);
            }

            Console.WriteLine("PASS: SnoopUI localized filter actions and literal search");
        }
        finally
        {
            InspectorLocalization.Default.SetLanguage(originalLanguage);
        }
    }

    private static void SetRoot(SnoopUI inspector, string propertyName, TreeItem root)
    {
        var property = typeof(SnoopUI).GetProperty(propertyName)
            ?? throw new InvalidOperationException("SnoopUI root property was not found: " + propertyName);
        property.SetValue(inspector, root);
    }

    private static void Require(bool result, string description)
    {
        if (!result)
        {
            throw new InvalidOperationException(description);
        }
    }

    private sealed class SmokeTreeItem : TreeItem
    {
        public SmokeTreeItem(object target, SmokeTreeItem? parent, TreeService treeService, bool hasBindingError = false)
            : base(target, parent, treeService)
        {
            this.HasBindingError = hasBindingError;
            parent?.AddChild(this);
        }

        public override bool HasBindingError { get; }
    }
}
