namespace Snoop.Host.SettingsSmoke;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Snoop.Controls;
using Snoop.Data.Tree;
using Snoop.Windows;

internal static class WorkbenchTreeOverflowSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var tree = (ProperTreeView)inspector.FindName("Tree");
        var root = new Border { Name = "DocumentView" };
        var parent = root;
        for (var i = 1; i <= 18; i++)
        {
            var child = new Border { Name = "ContentLayer" + i };
            parent.Child = child;
            parent = child;
        }

        parent.Name = "ReportDetails_" + new string('W', 112) + "_LastColumn";
        using var service = TreeService.From(TreeType.Visual);
        using var rootItem = service.Construct(root, null);
        var leaf = rootItem.FindNode(parent) ?? throw new InvalidOperationException("Missing deep target node.");
        leaf.ExpandTo();
        inspector.TreeItems.Add(rootItem);
        try
        {
            WorkbenchLayoutSmoke.Arrange(inspector);
            var viewer = (ScrollViewer)tree.Template.FindName("_tv_scrollviewer_", tree);
            var header = FindHeader(tree, leaf) ?? throw new InvalidOperationException("The expanded leaf was not rendered.");
            double RightEdge() => header.TranslatePoint(new Point(header.ActualWidth, 0), viewer).X;

            if (leaf.Depth != 18 || viewer.ComputedHorizontalScrollBarVisibility != Visibility.Visible
                || viewer.ScrollableWidth <= 0 || RightEdge() <= viewer.ActualWidth)
            {
                throw new InvalidOperationException($"The full expanded header must contribute to the tree scroll extent. Depth {leaf.Depth}, extent {viewer.ExtentWidth}, viewport {viewer.ViewportWidth}.");
            }

            viewer.ScrollToRightEnd();
            WorkbenchLayoutSmoke.Arrange(inspector);
            if (viewer.HorizontalOffset <= 0 || RightEdge() > viewer.ActualWidth + 1)
            {
                throw new InvalidOperationException($"The end of the deep node is clipped after scrolling. Header end {RightEdge()}, viewer {viewer.ActualWidth}, offset {viewer.HorizontalOffset}.");
            }
        }
        finally
        {
            inspector.TreeItems.Remove(rootItem);
        }

        Console.WriteLine("PASS: expanding an 18-level visual tree exposes the full long node label by horizontal scrolling.");
    }

    private static TextBlock? FindHeader(ItemsControl tree, TreeItem leaf)
    {
        var chain = new Stack<TreeItem>();
        for (TreeItem? item = leaf; item is not null; item = item.Parent)
        {
            chain.Push(item);
        }

        foreach (var item in chain)
        {
            if (tree.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container)
            {
                return null;
            }

            tree = container;
        }

        var header = (DependencyObject)tree.Template.FindName("PART_Header", tree);
        return FindText(header);

        TextBlock? FindText(DependencyObject visual)
        {
            if (visual is TextBlock block && Equals(block.DataContext, leaf))
            {
                return block;
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            {
                if (FindText(VisualTreeHelper.GetChild(visual, i)) is { } found)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
