// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop.Controls;

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using Snoop.Windows;

public class ProperTreeView : TreeView
{
    private const int MaxAboveOnReduce = 10;
    private const int MaxAboveOnWiden = 20;

    // A depth increase costs almost 4k of stack in a process under debugger
    private const int MinStackRequired = 0x10000; // 64k

    private SnoopUI? snoopUI;
    private ScrollViewer? scrollViewer;

    [ThreadStatic]
    private static IntPtr stackLimitLow;

    private static IntPtr StackLimitLow
    {
        get
        {
            if (stackLimitLow == IntPtr.Zero)
            {
                NativeMethods.GetCurrentThreadStackLimits(out stackLimitLow, out _);
            }

            return stackLimitLow;
        }
    }

    private static unsafe long GetRemainingStackSize()
    {
        var stackVar = stackalloc int[1];
        return stackVar - (int*)StackLimitLow;
    }

    // We need this method and what it does because:
    // If we have a tree which causes the scroll viewer to exceed a certain extent width we might get an StackOverflowException during measure/arrange.
    // To prevent these Exceptions (which immediately crash the program being snooped) we use the currently selected item, minus a few, as the new root node for the tree.
    // That way we get a "new" tree that is not as deeply nested as before, thus reducing the extent width of the scroll viewer.
    // If the currently selected item is the top most item in the current tree, we revert some of the reduction and
    public bool ApplyReduceDepthFilterIfNeeded(ProperTreeViewItem curNode)
    {
        if (this.snoopUI is null)
        {
            this.snoopUI = Window.GetWindow(this) as SnoopUI;

            if (this.snoopUI is null)
            {
                return false;
            }
        }

        if (this.snoopUI.IsReduceInProgress)
        {
            return true;
        }

        var curItem = (TreeItem)curNode.DataContext;
        var shouldReduce = GetRemainingStackSize() < MinStackRequired;
        var filtered = this.snoopUI.IsTreeFilterApplied;
        if (filtered && !shouldReduce)
        {
            // A matching result deliberately appears as a root even when selected.
            return false;
        }

        var item = curItem;
        if (!filtered && this.snoopUI.CurrentSelection is { } selected && item.Depth < selected.Depth)
        {
            item = selected;
        }

        if (item.Parent is null || this.Items.Count == 0)
        {
            return false;
        }

        var rootItem = filtered ? GetDisplayedRoot(curNode) : this.Items[0] as TreeItem;
        if (rootItem is null || (filtered && curItem == rootItem))
        {
            return false;
        }

        var shouldWiden = !shouldReduce && curNode.IsSelected && curItem == rootItem;
        if (shouldReduce == false
            && shouldWiden == false)
        {
            return false;
        }

        // Try to show some items above new root, that way we can keep a bit of context
        var newRoot = shouldWiden ? curItem.Parent : item.Parent;
        var levelsToShowAboveNewRoot = shouldWiden
            ? MaxAboveOnWiden
            : MaxAboveOnReduce;

        for (var i = 0; i < levelsToShowAboveNewRoot; ++i)
        {
            if (newRoot?.Parent is null || (filtered && newRoot == rootItem))
            {
                break;
            }

            newRoot = newRoot.Parent;
        }

        if (rootItem == newRoot)
        {
            return false;
        }

        this.snoopUI.ApplyReduceDepthFilter(newRoot, filtered ? rootItem : null);

        return true;
    }

    private static TreeItem? GetDisplayedRoot(TreeViewItem container)
    {
        // Model parents extend above the search results. Container parents stop at the displayed root.
        while (ItemsControl.ItemsControlFromItemContainer(container) is TreeViewItem parent)
        {
            container = parent;
        }

        return container.DataContext as TreeItem;
    }

    protected override DependencyObject GetContainerForItemOverride()
    {
        return new ProperTreeViewItem(new WeakReference(this));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        this.scrollViewer = this.Template.FindName("_tv_scrollviewer_", this) as ScrollViewer;
    }
}

public class ProperTreeViewItem : TreeViewItem
{
    private readonly WeakReference treeView;

    public ProperTreeViewItem(WeakReference treeView)
    {
        this.treeView = treeView;
    }

    public double Indent
    {
        get => GetIndent(this);
        set => SetIndent(this, value);
    }

    /// <summary>
    ///   Identifies the <see cref="Indent" /> attached dependency property.
    /// </summary>
    public static readonly DependencyProperty IndentProperty =
        DependencyProperty.RegisterAttached(
            nameof(Indent),
            typeof(double),
            typeof(ProperTreeViewItem));

    /// <summary>
    ///   Sets the <see cref="Indent" /> property value on the target element.
    /// </summary>
    public static void SetIndent(DependencyObject element, double value)
    {
        element.SetValue(IndentProperty, value);
    }

    /// <summary>
    ///   Gets the <see cref="Indent"/> property value of the target element.
    /// </summary>
    public static double GetIndent(DependencyObject element)
    {
        return (double)element.GetValue(IndentProperty);
    }

    protected override DependencyObject GetContainerForItemOverride()
    {
        var treeViewItem = new ProperTreeViewItem(this.treeView)
        {
            Indent = this.Indent + 12
        };
        return treeViewItem;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        // Check whether the tree is too deep.
        try
        {
            var targetTreeView = (ProperTreeView?)this.treeView.Target;
            if (targetTreeView is null
                || targetTreeView.ApplyReduceDepthFilterIfNeeded(this) == false)
            {
                return base.MeasureOverride(constraint);
            }
        }
        catch (Exception exception)
        {
            LogHelper.WriteWarning(exception);
        }

        return default;
    }

    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        // Check whether the tree is too deep.
        try
        {
            var targetTreeView = (ProperTreeView?)this.treeView.Target;
            if (targetTreeView is null
                || targetTreeView.ApplyReduceDepthFilterIfNeeded(this) == false)
            {
                return base.ArrangeOverride(arrangeBounds);
            }
        }
        catch (Exception exception)
        {
            LogHelper.WriteWarning(exception);
        }

        return default;
    }
}

public class IndentToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return new Thickness((double)value, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}