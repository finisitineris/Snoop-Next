namespace Snoop.Host.SettingsSmoke;

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using Snoop.Windows;
using CoreSettings = Snoop.Core.Settings;
using Rectangle = System.Windows.Shapes.Rectangle;

internal static class WorkbenchPreviewPickSmoke
{
    public static void Verify()
    {
        var selectPoint = typeof(ZoomerControl).GetMethod("SelectPreviewElementAt", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing the preview point selection handler.");

        var settings = CoreSettings.Default;
        var savedFile = settings.SettingsFile;
        var savedPlacement = settings.SnoopUIWindowPlacement;
        var savedTreeWidth = settings.WorkbenchTreeWidth;
        var savedPropertiesWidth = settings.WorkbenchPropertiesWidth;
        var savedDiagnosticsHeight = settings.WorkbenchDiagnosticsHeight;
        var savedTreeCollapsed = settings.WorkbenchTreeCollapsed;
        var savedPropertiesCollapsed = settings.WorkbenchPropertiesCollapsed;
        var savedDiagnosticsCollapsed = settings.WorkbenchDiagnosticsCollapsed;
        var savedShowDefaults = settings.ShowDefaults;
        var savedShowPreviewer = settings.ShowPreviewer;
        var savedTraceLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        var fixtureFile = Path.Combine(Path.GetDirectoryName(savedFile)!, "WorkbenchPreviewPickSmoke.xml");
        settings.SettingsFile = fixtureFile;
        try
        {
            var inspector = new SnoopUI();
            // The hidden inspector has no native Window source to acquire this usage count.
            CacheManager.Instance.IncreaseUsageCount();
            try
            {
                VerifyInspector(inspector, selectPoint);
            }
            finally
            {
                inspector.Close();
            }
        }
        finally
        {
            settings.SettingsFile = savedFile;
            settings.SnoopUIWindowPlacement = savedPlacement;
            settings.WorkbenchTreeWidth = savedTreeWidth;
            settings.WorkbenchPropertiesWidth = savedPropertiesWidth;
            settings.WorkbenchDiagnosticsHeight = savedDiagnosticsHeight;
            settings.WorkbenchTreeCollapsed = savedTreeCollapsed;
            settings.WorkbenchPropertiesCollapsed = savedPropertiesCollapsed;
            settings.WorkbenchDiagnosticsCollapsed = savedDiagnosticsCollapsed;
            settings.ShowDefaults = savedShowDefaults;
            settings.ShowPreviewer = savedShowPreviewer;
            PresentationTraceSources.DataBindingSource.Switch.Level = savedTraceLevel;
            File.Delete(fixtureFile);
            DrainDispatcher();
        }

        Console.WriteLine("PASS: preview clicks select and reveal the projected tree element while preserving scope, panes and zoom");
    }

    private static void VerifyInspector(SnoopUI inspector, MethodInfo selectPoint)
    {
        var preview = Find<Previewer>(inspector, "PreviewArea");
        var zoomer = Find<ZoomerControl>(preview, "Zoomer");
        var viewport = Find<Grid>(zoomer, "DocumentRoot");
        var viewbox = Find<Viewbox>(zoomer, "Viewbox");
        var outline = Find<Rectangle>(zoomer, "PreviewSelectionOutline");
        var path = Find<TextBlock>(inspector, "WorkbenchPreviewSelectionPath");
        var status = Find<TextBlock>(inspector, "WorkbenchSelectionValue");
        var properties = Find<PropertyInspector>(inspector, "PropertyInspector");
        var propertyTabs = Find<TabControl>(inspector, "PropertiesWorkbenchPane");
        var diagnosticTabs = Find<TabControl>(inspector, "DiagnosticsWorkbenchPane");
        var eventsTab = Find<TabItem>(inspector, "WorkbenchEventsTab");
        var treePane = Find<MenuItem>(inspector, "ShowTreePaneItem");
        var begin = FindMethod(typeof(ZoomerControl), "BeginPointerGesture");
        var move = FindMethod(typeof(ZoomerControl), "MovePointerGesture");
        var complete = FindMethod(typeof(ZoomerControl), "CompletePointerGesture");
        var cancel = FindMethod(typeof(ZoomerControl), "CancelPointerGesture");
        var setFilter = FindMethod(typeof(SnoopUI), "SetFilter");

        var scene = new Canvas { Width = 520, Height = 320, Background = Brushes.Beige };
        var scope = new Canvas
        {
            Name = "PickScope",
            Width = 300,
            Height = 210,
            RenderTransform = new ScaleTransform(1.15, 0.85),
        };
        var group = new Canvas { Name = "PickGroup", Width = 280, Height = 185 };
        var first = new Border { Name = "FirstSelection", Width = 56, Height = 33, Background = Brushes.CadetBlue };
        var templateVisual = new FrameworkElementFactory(typeof(Border), "PickTemplateLeaf");
        templateVisual.SetValue(Border.BackgroundProperty, Brushes.Coral);
        var button = new Button
        {
            Name = "PreviewAction",
            Width = 81,
            Height = 47,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = templateVisual },
        };
        var activated = 0;
        button.Click += (_, _) => activated++;
        Place(scene, scope, 63, 52);
        Place(scene, new Border { Name = "OutsideScope", Width = 60, Height = 45, Background = Brushes.Green }, 430, 90);
        Place(scope, group, 14, 23);
        Place(group, first, 17, 21);
        Place(group, button, 135, 77);
        using var source = new HwndSource(new HwndSourceParameters("Snoop preview picking smoke")
        {
            Width = 520,
            Height = 320,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000),
        });
        source.RootVisual = scene;
        WorkbenchLayoutSmoke.Arrange(scene, new Rect(0, 0, scene.Width, scene.Height));
        var templateLeaf = button.Template.FindName("PickTemplateLeaf", button) as Border
            ?? throw new InvalidOperationException("Missing the actual Button template visual.");
        templateLeaf.Name = "PickTemplateLeaf";

        try
        {
            zoomer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, zoomer));
            preview.SetCurrentValue(Previewer.IsActiveProperty, true);
            inspector.ScopeTo(scope);
            SnoopUI.RefreshCommand.Execute(null, inspector);
            DrainDispatcher();
            var service = inspector.TreeService;
            var root = inspector.RootTreeItem ?? throw new InvalidOperationException("Missing the scoped preview tree.");
            var firstNode = FindNode(root, first);
            var buttonNode = FindNode(root, button);
            var templateNode = FindNode(root, templateLeaf);
            inspector.CurrentSelection = firstNode;
            propertyTabs.SelectedIndex = 1;
            diagnosticTabs.SelectedItem = eventsTab;
            properties.FilterString = "Width";
            ApplyFilter(group.Name);
            treePane.IsChecked = false;
            treePane.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, treePane));
            Arrange();

            var transforms = (TransformGroup)viewbox.RenderTransform;
            var scale = (ScaleTransform)transforms.Children[0];
            var translation = (TranslateTransform)transforms.Children[1];
            scale.ScaleX = 1.15;
            scale.ScaleY = 1.15;
            translation.X = -15;
            translation.Y = 8;
            Arrange();
            var point = PointFor(templateLeaf);

            // Simulate the gesture state transitions without moving the real cursor.
            Gesture(point, MouseButton.Right);
            Gesture(point, MouseButton.Left, fromImage: false);
            selectPoint.Invoke(zoomer, new object[] { new Point(-1, -1) });
            begin.Invoke(zoomer, new object[] { point, MouseButton.Left, true });
            cancel.Invoke(zoomer, null);
            complete.Invoke(zoomer, new object[] { point, MouseButton.Left });
            begin.Invoke(zoomer, new object[] { point, MouseButton.Left, true });
            move.Invoke(zoomer, new object[] { point + new Vector(SystemParameters.MinimumHorizontalDragDistance + 18, 0) });
            move.Invoke(zoomer, new object[] { point });
            complete.Invoke(zoomer, new object[] { point, MouseButton.Left });
            Arrange();
            Require(ReferenceEquals(inspector.CurrentSelection, firstNode),
                "Right click, a non-image press, an outside point, a canceled click and a pan must leave selection alone.");

            var panX = translation.X;
            var panY = translation.Y;
            inspector.SkipTemplateParts = false;
            Gesture(PointFor(templateLeaf), MouseButton.Left);
            Arrange();
            VerifySelection(templateNode, "PickScope › PickGroup › PreviewAction › PickTemplateLeaf", group.Name);

            inspector.SkipTemplateParts = true;
            Gesture(PointFor(templateLeaf), MouseButton.Left);
            Arrange();
            VerifySelection(buttonNode, "PickScope › PickGroup › PreviewAction", group.Name);
            Require(scale.ScaleX == 1.15 && scale.ScaleY == 1.15 && translation.X == panX && translation.Y == panY,
                "Picking a visual from the same scene must retain the preview zoom and translation.");

            ApplyFilter(first.Name);
            inspector.CurrentSelection = firstNode;
            Require(inspector.TreeItems.Count == 1 && ReferenceEquals(inspector.TreeItems[0], firstNode),
                "The obstruction fixture must hide the Button from the filtered tree.");
            Gesture(PointFor(templateLeaf), MouseButton.Left);
            Arrange();
            VerifySelection(buttonNode, "PickScope › PickGroup › PreviewAction", string.Empty);
            Require(inspector.TreeItems.Contains(root), "Picking a hidden item must restore the unfiltered current tree.");

            inspector.CurrentSelection = firstNode;
            Arrange();
            var stalePoint = PointFor(templateLeaf);
            preview.SetCurrentValue(Previewer.IsActiveProperty, false);
            selectPoint.Invoke(zoomer, new object[] { stalePoint });
            Arrange();
            Require(ReferenceEquals(inspector.CurrentSelection, firstNode), "A disabled preview must reject picking.");
            preview.SetCurrentValue(Previewer.IsActiveProperty, true);
            Arrange();

            stalePoint = PointFor(templateLeaf);
            begin.Invoke(zoomer, new object[] { stalePoint, MouseButton.Left, true });
            source.RootVisual = null;
            complete.Invoke(zoomer, new object[] { stalePoint, MouseButton.Left });
            Arrange();
            Require(ReferenceEquals(inspector.CurrentSelection, firstNode) && activated == 0,
                "A detached preview cannot pick from its old scene or invoke a target control.");

            void VerifySelection(TreeItem expected, string breadcrumb, string filter)
            {
                Require(ReferenceEquals(inspector.CurrentSelection, expected) && expected.IsSelected
                        && ReferenceEquals(preview.Target, expected.Target) && ReferenceEquals(properties.RootTarget, expected.Target)
                        && path.Text == breadcrumb && status.Text == expected.DisplayName && outline.Visibility == Visibility.Visible,
                    $"A preview pick must synchronize the tree, property target, path, status and highlight. "
                    + $"Expected {breadcrumb}, selection {inspector.CurrentSelection?.DisplayName}, path {path.Text}, status {status.Text}, "
                    + $"selected {expected.IsSelected}, preview {ReferenceEquals(preview.Target, expected.Target)}, "
                    + $"properties {ReferenceEquals(properties.RootTarget, expected.Target)}, outline {outline.Visibility}.");
                Require(ReferenceEquals(inspector.RootTreeItem, root) && ReferenceEquals(inspector.TreeService, service)
                        && ReferenceEquals(inspector.ScopedRootObject, scope) && ReferenceEquals(preview.ScopeTarget, scope)
                        && inspector.Filter == filter && properties.FilterString == "Width" && !treePane.IsChecked
                        && propertyTabs.SelectedIndex == 1 && ReferenceEquals(diagnosticTabs.SelectedItem, eventsTab) && activated == 0,
                    "A preview pick must preserve scope, relevant filters, pane choices and the inspectee's input.");
            }
        }
        finally
        {
            zoomer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, zoomer));
            source.RootVisual = null;
            DrainDispatcher();
        }

        void Arrange() => WorkbenchLayoutSmoke.Arrange(inspector);

        void ApplyFilter(string text)
        {
            setFilter.Invoke(inspector, new object[] { text });
            DrainDispatcher();
        }

        void Gesture(Point position, MouseButton button, bool fromImage = true)
        {
            begin.Invoke(zoomer, new object[] { position, button, fromImage });
            complete.Invoke(zoomer, new object[] { position, button });
        }

        Point PointFor(FrameworkElement target)
        {
            if (viewbox.Child is not Rectangle { Fill: VisualBrush { Visual: UIElement context } brush } projection)
            {
                throw new InvalidOperationException("The preview must have a live projected scene before picking.");
            }

            var local = target.TransformToAncestor(context).Transform(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
            var toBrush = VisualTreeHelper.GetTransform(context)?.Value ?? Matrix.Identity;
            var offset = VisualTreeHelper.GetOffset(context);
            toBrush.Translate(offset.X, offset.Y);
            var point = toBrush.Transform(local);
            var crop = brush.Viewbox;
            var projected = new Point((point.X - crop.X) * projection.ActualWidth / crop.Width,
                (point.Y - crop.Y) * projection.ActualHeight / crop.Height);
            return projection.TransformToAncestor(viewport).Transform(projected);
        }
    }

    private static void Place(Canvas parent, FrameworkElement child, double x, double y)
    {
        Canvas.SetLeft(child, x);
        Canvas.SetTop(child, y);
        parent.Children.Add(child);
    }

    private static TreeItem FindNode(TreeItem root, FrameworkElement element)
        => root.FindNode(element) ?? throw new InvalidOperationException("Missing fixture tree node " + element.Name + ".");

    private static T Find<T>(FrameworkElement parent, string name)
        where T : class
        => parent.FindName(name) as T ?? throw new InvalidOperationException("Missing preview picking control " + name + ".");

    private static MethodInfo FindMethod(Type type, string name)
        => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
           ?? throw new InvalidOperationException("Missing preview picking method " + name + ".");

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
