namespace Snoop.Host.SettingsSmoke;

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Windows.Threading;
using Snoop.Core;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Windows;

internal static class TreeNodeMenuSmoke
{
    public static void Verify(SnoopUI inspector, ThemeMode theme)
    {
        var target = new StackPanel { Name = "DocumentView" };
        var header = new Border { Name = "DocumentHeader" };
        var body = new Grid { Name = "DocumentBody" };
        target.Children.Add(header);
        target.Children.Add(body);
        using var service = TreeService.From(TreeType.Visual);
        using var root = service.Construct(target, null, isScoped: false);
        root.Reload();
        var tree = (TreeView)inspector.FindName("Tree");
        var language = InspectorLocalization.Default.LanguageTag;
        inspector.TreeItems.Add(root);
        try
        {
            Drain();
            var container = GetContainer(tree, root);
            container.IsExpanded = true;
            Drain();
            Open(header, verifySubmenu: true);
            Open(body, verifySubmenu: false);

            void Open(FrameworkElement element, bool verifySubmenu)
            {
                var node = root.Children.Single(child => ReferenceEquals(child.Target, element));
                var row = GetContainer(container, node);
                var menu = row.ContextMenu;
                var oldTarget = menu.PlacementTarget;
                var oldPlacement = menu.Placement;
                try
                {
                    menu.PlacementTarget = row;
                    menu.Placement = PlacementMode.Bottom;
                    menu.IsOpen = true;
                    Drain();
                    var export = (MenuItem)menu.Items[0];
                    var scope = (MenuItem)menu.Items[1];
                    if (!Equals(export.Header, "导出") || !Equals(scope.Header, "仅查看此子树")
                        || !ReferenceEquals(scope.Command, SnoopUI.ScopeToCommand) || !ReferenceEquals(scope.CommandParameter, node)
                        || export.Icon is not Path || scope.Icon is not Path)
                    {
                        throw new InvalidOperationException("The tree popup must use localized Fluent commands for its current row.");
                    }

                    if (verifySubmenu)
                    {
                        export.IsSubmenuOpen = true;
                        var flyout = InspectorFlyoutSmoke.VerifyPopup(export, inspector);
                        var variants = export.Items.OfType<MenuItem>().Where(item => item.Command == SnoopUI.ExportTreeWithFilterCommand)
                            .Select(item => (ExportOptions)item.CommandParameter).ToArray();
                        if (variants.Length != 4 || variants.Any(option => !ReferenceEquals(option.TreeItem, node))
                            || variants.Select(option => (option.Recurse ? 2 : 0) + (option.UseFilter ? 1 : 0)).Distinct().Count() != 4)
                        {
                            throw new InvalidOperationException("The export options must retain their clicked node, recursion and filter settings.");
                        }

                        InspectorFlyoutSmoke.Capture((FrameworkElement)inspector.FindName("WorkbenchChrome"), "tree-menu-" + theme, menu, flyout);
                        InspectorLocalization.Default.SetLanguage("en-US");
                        Drain();
                        if (!Equals(export.Header, "Export") || !Equals(scope.Header, "Scope to this subtree"))
                        {
                            throw new InvalidOperationException("An already-open tree menu must follow the inspector language.");
                        }

                        export.IsSubmenuOpen = false;
                    }
                }
                finally
                {
                    InspectorLocalization.Default.SetLanguage(language);
                    menu.IsOpen = false;
                    menu.PlacementTarget = oldTarget;
                    menu.Placement = oldPlacement;
                    Drain();
                }
            }
        }
        finally
        {
            inspector.TreeItems.Remove(root);
        }
    }

    private static TreeViewItem GetContainer(ItemsControl parent, TreeItem node)
    {
        parent.UpdateLayout();
        return parent.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem
            ?? throw new InvalidOperationException("The expanded tree must provide a visible row for the node menu.");
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
}
