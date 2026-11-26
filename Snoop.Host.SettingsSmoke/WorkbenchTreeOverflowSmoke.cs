namespace Snoop.Host.SettingsSmoke;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Data.Tree;
using Snoop.Localization;
using Snoop.Windows;

internal static class WorkbenchTreeOverflowSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var tree = (ProperTreeView)inspector.FindName("Tree");
        var chrome = (FrameworkElement)inspector.FindName("WorkbenchChrome");
        var language = InspectorLocalization.Default.LanguageTag;
        var originalSelection = inspector.CurrentSelection;
        var propertiesToggle = (MenuItem)inspector.FindName("ShowPropertiesPaneItem");
        var propertiesShown = propertiesToggle.IsChecked;
        var document = new Grid { Name = "RootGrid" };
        var panel = document;
        var layers = new[]
        {
            "DocumentShell", "EditorArea", "SessionView", "TimelineWorkspace", "TimelineSurface",
            "TracksPresenter", "ProcessGroup", "RenderProcess", "ThreadTracks", "RenderThread", "FrameDetails",
            "FrameInspector", "CountersPanel", "MeasurementTable", "RowsPresenter", "FrameTimingRow", "DurationCell",
        };
        document.Children.Add(new Border { Name = "SessionToolbar" });
        foreach (var name in layers)
        {
            if (name == "ThreadTracks" || name == "RowsPresenter")
            {
                panel.Children.Add(new Border { Name = name + "Header" });
            }

            var child = new Grid { Name = name };
            panel.Children.Add(child);
            panel = child;
        }

        var text = new TextBlock
        {
            Name = "FrameTimingDetails_ManagedThreadDispatch_PerformanceCounters_RequestedRenderingDurationMicroseconds_PerTimelineEvent_BeforePresent_LastVisibleColumn",
            Text = "14.82 ms",
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(text);
        WorkbenchLayoutSmoke.Arrange(document, new Rect(0, 0, 320, 160));
        using var service = TreeService.From(TreeType.Visual);
        using var rootItem = service.Construct(document, null);
        var leaf = rootItem.FindNode(text) ?? throw new InvalidOperationException("Missing deep target node.");
        leaf.ExpandTo();
        inspector.TreeItems.Add(rootItem);
        try
        {
            SetPropertiesShown(false);
            InspectorLocalization.Default.SetLanguage("zh-CN");
            inspector.CurrentSelection = leaf;
            Arrange();
            var viewer = (ScrollViewer)tree.Template.FindName("_tv_scrollviewer_", tree);
            var header = FindHeader(tree, leaf) ?? throw new InvalidOperationException("The expanded leaf was not rendered.");
            viewer.ScrollToLeftEnd();
            Arrange();
            double RightEdge() => header.TranslatePoint(new Point(header.ActualWidth, 0), viewer).X;

            if (leaf.Depth != 18 || viewer.ComputedHorizontalScrollBarVisibility != Visibility.Visible
                || viewer.ScrollableWidth <= 0 || RightEdge() <= viewer.ActualWidth)
            {
                var rootRow = (FrameworkElement)tree.ItemContainerGenerator.ContainerFromItem(rootItem);
                throw new InvalidOperationException($"The full expanded header must contribute to the tree scroll extent. Depth {leaf.Depth}, extent {viewer.ExtentWidth}x{viewer.ExtentHeight}, viewport {viewer.ViewportWidth}x{viewer.ViewportHeight}, bar {viewer.HorizontalScrollBarVisibility}/{viewer.ComputedHorizontalScrollBarVisibility}, leaf right {RightEdge()}, root desired {rootRow.DesiredSize}, root rendered {rootRow.RenderSize}.");
            }

            viewer.ScrollToRightEnd();
            Arrange();
            if (viewer.HorizontalOffset <= 0 || RightEdge() > viewer.ActualWidth + 1)
            {
                throw new InvalidOperationException($"The end of the deep node is clipped after scrolling. Header end {RightEdge()}, viewer {viewer.ActualWidth}, offset {viewer.HorizontalOffset}.");
            }

            LauncherAppearanceSmoke.CaptureIfRequested(chrome, "expanded-visual-tree-scrolled");
            viewer.ScrollToLeftEnd();
            Arrange();
            LauncherAppearanceSmoke.CaptureIfRequested(chrome, "expanded-visual-tree");
            VerifyRootExpansion();
            VerifyDeferredToolTip();
        }
        finally
        {
            SetPropertiesShown(propertiesShown);
            inspector.CurrentSelection = originalSelection;
            inspector.TreeItems.Remove(rootItem);
            InspectorLocalization.Default.SetLanguage(language);
        }

        Console.WriteLine("PASS: RootGrid collapse and expansion, full 18-level label scrolling and on-demand tree preview brushes.");

        void Arrange()
        {
            inspector.UpdateLayout();
            inspector.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        }

        void VerifyRootExpansion()
        {
            var rootRow = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(rootItem);
            var rowPeer = UIElementAutomationPeer.CreatePeerForElement(rootRow);
            var expansion = (IExpandCollapseProvider)rowPeer.GetPattern(PatternInterface.ExpandCollapse);
            var expander = (ToggleButton)rootRow.Template.FindName("Expander", rootRow);
            var toggle = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(expander).GetPattern(PatternInterface.Toggle);
            var expectedRows = rootItem.Children.Concat(new[] { leaf }).ToArray();

            VerifyRows(true);
            toggle.Toggle();
            Arrange();
            VerifyRows(false);
            toggle.Toggle();
            Arrange();
            VerifyRows(true);

            void VerifyRows(bool expanded)
            {
                var expectedState = expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
                if (!expander.IsVisible || rowPeer.GetAutomationId() != document.Name || expansion.ExpandCollapseState != expectedState
                    || rootItem.IsExpanded != expanded)
                {
                    throw new InvalidOperationException($"RootGrid must remain visible and report {expectedState} through the tree and UI Automation.");
                }

                // A collapsed branch can retain its containers. Check their effective visibility and
                // resolve each header again after expansion so recycled rows must display the same nodes.
                foreach (var item in expectedRows)
                {
                    var rowHeader = FindHeader(tree, item);
                    if ((rowHeader?.IsVisible == true) != expanded || (expanded && rowHeader?.Text != item.ToString()))
                    {
                        throw new InvalidOperationException($"Unexpected visibility or caption for '{item.Name}' when RootGrid is {expectedState}.");
                    }
                }
            }
        }

        void SetPropertiesShown(bool shown)
        {
            if (propertiesToggle.IsChecked != shown)
            {
                propertiesToggle.IsChecked = shown;
                propertiesToggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, propertiesToggle));
            }
        }

        void VerifyDeferredToolTip()
        {
            using var node = new PreviewProbe(new Border(), service);
            inspector.TreeItems.Add(node);
            try
            {
                Arrange();
                var row = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(node);
                if (node.BrushReads != 0 || !ReferenceEquals(row.ToolTip, node))
                {
                    throw new InvalidOperationException("Realizing a tree row must not create its preview brush.");
                }

                var tip = new ToolTip { Style = (Style)row.FindResource(typeof(ToolTip)), Content = row.ToolTip };
                WorkbenchLayoutSmoke.Arrange(tip, new Rect(0, 0, 200, 200));
                if (tip.Template.FindName("PreviewImage", tip) is not System.Windows.Shapes.Rectangle { Fill: VisualBrush brush }
                    || node.BrushReads == 0 || !ReferenceEquals(brush.Visual, node.Target))
                {
                    throw new InvalidOperationException("The tree tooltip template must render its own node when requested.");
                }
            }
            finally
            {
                inspector.TreeItems.Remove(node);
            }
        }
    }

    private sealed class PreviewProbe : DependencyObjectTreeItem
    {
        public PreviewProbe(Border target, TreeService tree)
            : base(target, null, tree, false)
        {
        }

        public int BrushReads { get; private set; }

        public override Brush? VisualBrush
        {
            get
            {
                this.BrushReads++;
                return base.VisualBrush;
            }
        }
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

        return tree.Template?.FindName("PART_Header", tree) is DependencyObject header ? FindText(header) : null;

        TextBlock? FindText(DependencyObject visual)
        {
            if (visual is TextBlock block && ReferenceEquals(block.DataContext, leaf))
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
