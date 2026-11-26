namespace Snoop.Host.SettingsSmoke;

using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Data.Tree;
using Snoop.Windows;

internal static class WorkbenchPreviewHighlightSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var preview = Find<Previewer>(inspector, "PreviewArea");
        var zoomer = Find<ZoomerControl>(preview, "Zoomer");
        var outline = Find<Rectangle>(zoomer, "PreviewSelectionOutline");
        var viewport = Find<Grid>(zoomer, "DocumentRoot");
        var viewbox = Find<Viewbox>(zoomer, "Viewbox");
        var overlay = outline.Parent as Canvas
            ?? throw new InvalidOperationException("The preview selection outline must use a viewport overlay.");
        Require(ReferenceEquals(overlay.Parent, viewport) && ReferenceEquals(viewbox.Parent, viewport)
                && !outline.IsHitTestVisible && !overlay.IsHitTestVisible,
            "The preview outline must overlay the zoomable image without intercepting input.");
        Require(outline.StrokeThickness == 2
                && outline.Stroke is SolidColorBrush stroke
                && inspector.TryFindResource("SnoopNext.Brush.Accent") is SolidColorBrush accent
                && stroke.Color == accent.Color,
            "The preview outline must use the workbench accent at a consistent thickness.");

        var targetRoot = new Canvas { Width = 480, Height = 320, Background = Brushes.Beige };
        var scope = new Canvas
        {
            Width = 300,
            Height = 210,
            RenderTransform = new ScaleTransform(1.125, 0.875),
            Background = Brushes.Navy,
        };
        var group = new Canvas();
        var first = new Border { Width = 52, Height = 26, Background = Brushes.CadetBlue };
        var second = new Border { Width = 82, Height = 42, Background = Brushes.Coral };
        Place(targetRoot, scope, 80, 42);
        Place(scope, group, 20, 25);
        Place(group, first, 8, 18);
        Place(group, second, 104, 70);
        using var targetSource = new HwndSource(new HwndSourceParameters("Snoop preview projection smoke")
        {
            Width = 480,
            Height = 320,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000),
        });
        targetSource.RootVisual = targetRoot;
        WorkbenchLayoutSmoke.Arrange(targetRoot, new Rect(0, 0, targetRoot.Width, targetRoot.Height));
        Require(ReferenceEquals(PresentationSource.FromVisual(first), targetSource)
                && ReferenceEquals(PresentationSource.FromVisual(second), targetSource),
            "The projection fixture needs a live, separate WPF presentation source.");

        var rootProperty = typeof(SnoopUI).GetProperty(nameof(SnoopUI.RootTreeItem))
            ?? throw new InvalidOperationException("Missing the inspector root for the preview projection fixture.");
        var savedRoot = inspector.RootTreeItem;
        var savedSelection = inspector.CurrentSelection;
        var savedActive = preview.IsActive;
        using var tree = TreeService.From(TreeType.Visual);
        using var fullRoot = new TreeItem(targetRoot, null, tree);
        using var fullScope = new TreeItem(scope, fullRoot, tree);
        using var fullGroup = new TreeItem(group, fullScope, tree);
        using var fullFirst = new TreeItem(first, fullGroup, tree);
        using var scopedRoot = new TreeItem(scope, null, tree, isScoped: true);
        using var scopedGroup = new TreeItem(group, scopedRoot, tree);
        using var scopedFirst = new TreeItem(first, scopedGroup, tree);
        using var scopedSecond = new TreeItem(second, scopedGroup, tree);
        using var nonvisual = new TreeItem(Brushes.Coral, scopedRoot, tree);
        inspector.TreeItems.Add(fullRoot);
        inspector.TreeItems.Add(scopedRoot);

        try
        {
            // Run the preview's layout updates while keeping the inspector window hidden.
            zoomer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, zoomer));
            preview.SetCurrentValue(Previewer.IsActiveProperty, true);
            inspector.CurrentSelection = null;
            Arrange();
            Require(outline.Visibility == Visibility.Collapsed, "The preview outline must start hidden without a selection.");

            rootProperty.SetValue(inspector, fullRoot);
            inspector.CurrentSelection = fullFirst;
            Arrange();
            VerifyProjection(targetRoot, first);

            rootProperty.SetValue(inspector, scopedRoot);
            inspector.CurrentSelection = scopedFirst;
            Arrange();
            Require(ReferenceEquals(preview.ScopeTarget, scope),
                "The actual workbench preview must follow the scoped tree root.");
            var firstBounds = VerifyProjection(scope, first);
            VerifyProjectedPixel(viewport, firstBounds, Colors.CadetBlue);
            Require(Previewer.MagnifyCommand.CanExecute(null, preview)
                    && Previewer.ScreenshotCommand.CanExecute(null, preview),
                "The magnifier and screenshot commands must remain available for the selected visual.");
            VerifyBackdropControls(inspector, preview, viewport);

            inspector.CurrentSelection = scopedSecond;
            Arrange();
            var secondBounds = VerifyProjection(scope, second);
            Require(secondBounds.Left > firstBounds.Left && secondBounds.Top > firstBounds.Top
                    && secondBounds.Width > firstBounds.Width && secondBounds.Height > firstBounds.Height,
                "Switching from the first to the second element must move and resize the projected outline.");

            Canvas.SetLeft(second, 134);
            Canvas.SetTop(second, 48);
            second.Width = 94;
            second.Height = 33;
            Arrange();
            var movedBounds = VerifyProjection(scope, second);
            Require(ReferenceEquals(inspector.CurrentSelection, scopedSecond)
                    && movedBounds.Left > secondBounds.Left && movedBounds.Top < secondBounds.Top,
                "The outline must follow a selected element's layout changes without a new selection.");

            Canvas.SetLeft(second, 245);
            Arrange();
            Require(outline.Visibility == Visibility.Visible && outline.Clip is RectangleGeometry clip
                    && clip.Rect.Width > 0 && clip.Rect.Right < outline.Width - 1,
                "Partly hidden content must clip the outline without drawing a new edge along the preview crop.");
            Canvas.SetLeft(second, 134);
            Arrange();

            WorkbenchLayoutSmoke.Arrange(inspector, 820, 530);
            var resizedBounds = VerifyProjection(scope, second);
            var transforms = (TransformGroup)viewbox.RenderTransform;
            var scale = (ScaleTransform)transforms.Children[0];
            var translation = (TranslateTransform)transforms.Children[1];
            scale.ScaleX = 1.17;
            scale.ScaleY = 1.17;
            translation.X = -12;
            translation.Y = 9;
            viewport.InvalidateArrange();
            WorkbenchLayoutSmoke.Arrange(inspector, 820, 530);
            var zoomedBounds = VerifyProjection(scope, second);
            Require(zoomedBounds.Width > resizedBounds.Width,
                "The same outline must follow zoom and translation applied to the projected scene.");

            preview.SetCurrentValue(Previewer.IsActiveProperty, false);
            Arrange();
            VerifyFallback(scope);
            Require(ReferenceEquals(preview.Target, second), "Turning off preview must preserve the selected command target.");
            preview.SetCurrentValue(Previewer.IsActiveProperty, true);
            Arrange();
            VerifyProjection(scope, second);

            group.Children.Remove(second);
            Arrange();
            VerifyFallback(scope);
            group.Children.Add(second);
            Arrange();
            VerifyProjection(scope, second);

            inspector.CurrentSelection = nonvisual;
            Arrange();
            VerifyFallback(scope);
            Require(ReferenceEquals(preview.Target, nonvisual.Target),
                "A nonvisual selection must remain the same selection throughout the workbench.");
            inspector.CurrentSelection = null;
            Arrange();
            VerifyFallback(scope);
        }
        finally
        {
            zoomer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, zoomer));
            inspector.CurrentSelection = null;
            inspector.TreeItems.Remove(scopedRoot);
            inspector.TreeItems.Remove(fullRoot);
            rootProperty.SetValue(inspector, savedRoot);
            inspector.CurrentSelection = savedSelection;
            preview.SetCurrentValue(Previewer.IsActiveProperty, savedActive);
            targetSource.RootVisual = null;
            DrainDispatcher();
        }

        Console.WriteLine("PASS: preview projects the live scene and tracks the selected element through scope, layout and zoom changes");

        void Arrange()
        {
            targetRoot.UpdateLayout();
            WorkbenchLayoutSmoke.Arrange(inspector);
        }

        Rect VerifyProjection(FrameworkElement scene, FrameworkElement selected)
        {
            if (viewbox.Child is not Rectangle { Fill: VisualBrush brush } image)
            {
                throw new InvalidOperationException("The preview must display the live visual scene.");
            }

            Require(ReferenceEquals(brush.Visual, scene) && brush.ViewboxUnits == BrushMappingMode.Absolute
                    && ReferenceEquals(preview.Target, selected) && ReferenceEquals(zoomer.Target, selected),
                "The preview must display the correct scene while preserving the selected visual as the command target.");
            Require(outline.Visibility == Visibility.Visible && !targetRoot.IsAncestorOf(outline)
                    && ReferenceEquals(VisualTreeHelper.GetParent(selected), group),
                "Only the preview workspace should contain the visible selection outline.");

            var sceneMatrix = VisualTreeHelper.GetTransform(scene)?.Value ?? Matrix.Identity;
            var sceneOffset = VisualTreeHelper.GetOffset(scene);
            sceneMatrix.Translate(sceneOffset.X, sceneOffset.Y);
            var sceneTransform = new MatrixTransform(sceneMatrix);
            var crop = sceneTransform.TransformBounds(new Rect(scene.RenderSize));
            RequireClose(brush.Viewbox, crop, "VisualBrush crop");

            var selectedInScene = selected.TransformToAncestor(scene).TransformBounds(new Rect(selected.RenderSize));
            var selectedInBrush = sceneTransform.TransformBounds(selectedInScene);
            var scaleX = image.RenderSize.Width / crop.Width;
            var scaleY = image.RenderSize.Height / crop.Height;
            var selectedOnImage = new Rect((selectedInBrush.X - crop.X) * scaleX, (selectedInBrush.Y - crop.Y) * scaleY,
                selectedInBrush.Width * scaleX, selectedInBrush.Height * scaleY);
            var expected = image.TransformToAncestor(viewport).TransformBounds(selectedOnImage);
            var actual = new Rect(Canvas.GetLeft(outline), Canvas.GetTop(outline), outline.Width, outline.Height);
            RequireClose(actual, expected, "Projected selection outline");
            return actual;
        }

        void VerifyFallback(Visual scene)
        {
            Require(outline.Visibility == Visibility.Collapsed
                    && (viewbox.Child is not Rectangle { Fill: VisualBrush brush } || !ReferenceEquals(brush.Visual, scene)),
                "An inactive, disconnected or nonvisual selection must clear the scene outline and previous projection.");
        }
    }

    private static void VerifyBackdropControls(SnoopUI inspector, Previewer preview, Grid viewport)
    {
        var slider = Find<Slider>(preview, "ColorSlider");
        var magnify = Find<Button>(preview, "MagnifyButton");
        var originalWidth = preview.MaxWidth;
        var range = (IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(slider).GetPattern(PatternInterface.RangeValue);
        var brightness = range.Value;
        var placement = Snoop.Core.Settings.Default.ZoomerWindowPlacement;
        Zoomer? viewer = null;
        try
        {
            Require(!viewport.IsAncestorOf(slider) && slider.IsHitTestVisible && slider.IsEnabled
                    && ReferenceEquals(magnify.Command, Previewer.MagnifyCommand),
                "The backdrop slider must accept input outside the preview's pick and pan surface.");
            range.SetValue(0.25);
            DrainDispatcher();
            Require(viewport.Background is SolidColorBrush brush && Math.Abs(brush.Color.ScR - 0.25) < 0.001
                    && brush.Color.ScG == brush.Color.ScR && brush.Color.ScB == brush.Color.ScR,
                "The backdrop range control must change the live preview's grayscale background.");
            VerifyBackdropPosition(preview);
            preview.SetCurrentValue(FrameworkElement.MaxWidthProperty, 160d);
            WorkbenchLayoutSmoke.Arrange(inspector);
            VerifyBackdropPosition(preview);
            Require(slider.ActualWidth is >= 32 and < 100, "The backdrop slider must shrink in a narrow preview.");

            Previewer.MagnifyCommand.Execute(null, magnify);
            viewer = Application.Current.Windows.OfType<Zoomer>().Single(window => ReferenceEquals(window.Target, preview.Target));
            Require(Find<Slider>(viewer, "ColorSlider").Value == range.Value,
                "Opening the magnifier must retain the selected preview backdrop.");
            CaptureBackdropControls(preview, range.Value);
        }
        finally
        {
            viewer?.Close();
            Snoop.Core.Settings.Default.ZoomerWindowPlacement = placement;
            range.SetValue(brightness);
            preview.SetCurrentValue(FrameworkElement.MaxWidthProperty, originalWidth);
            WorkbenchLayoutSmoke.Arrange(inspector);
        }
    }

    private static Rect VerifyBackdropPosition(Previewer preview)
    {
        var slider = Find<Slider>(preview, "ColorSlider");
        var magnify = Find<Button>(preview, "MagnifyButton");
        var backdrop = slider.TransformToAncestor(preview).TransformBounds(new Rect(slider.RenderSize));
        var button = magnify.TransformToAncestor(preview).TransformBounds(new Rect(magnify.RenderSize));
        var bounds = new Rect(preview.RenderSize);
        Require(bounds.Contains(backdrop) && bounds.Contains(button) && Math.Abs(backdrop.Right + 4 - button.Left) <= 1
                && Math.Abs(backdrop.Top - button.Top) <= 1 && Math.Abs(backdrop.Bottom - button.Bottom) <= 1
                && Math.Abs(bounds.Bottom - button.Bottom - 4) <= 1,
            $"The backdrop slider {backdrop} must sit directly left of the magnifier {button} in the bottom toolbar of {bounds}.");
        return backdrop;
    }

    private static void CaptureBackdropControls(Previewer workbenchPreview, double brightness)
    {
        if (Environment.GetEnvironmentVariable("SNOOP_UI_PREVIEW_DIR") is not { Length: > 0 } directory)
        {
            return;
        }

        // A separate visible window supplies the normal preview styles and a stable
        // capture area independently of the workbench's persisted splitter sizes.
        var preview = new Previewer { ScopeTarget = workbenchPreview.ScopeTarget, Target = workbenchPreview.Target };
        var surface = new Grid
        {
            Width = 320, Height = 200,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        surface.Children.Add(preview);
        var host = new SnoopBaseWindow
        {
            Content = surface, SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false,
        };
        try
        {
            host.Show();
            Find<Slider>(preview, "ColorSlider").SetCurrentValue(Slider.ValueProperty, brightness);
            Capture(320, "preview-backdrop-controls");
            Capture(160, "preview-backdrop-controls-narrow");
        }
        finally
        {
            host.Close();
        }

        void Capture(int width, string name)
        {
            surface.Width = width;
            host.UpdateLayout();
            DrainDispatcher();
            Require(preview.IsVisible && preview.ActualWidth == width && preview.ActualHeight == surface.Height,
                $"The capture window must show a {width} × {surface.Height} preview, got {preview.RenderSize}.");
            var backdrop = VerifyBackdropPosition(preview);
            LauncherAppearanceSmoke.CaptureIfRequested(surface, name);
            using var stream = File.OpenRead(System.IO.Path.Combine(directory, name + ".png"));
            var image = new FormatConvertedBitmap(BitmapFrame.Create(stream), PixelFormats.Bgra32, null, 0);
            var pixels = new int[image.PixelWidth * image.PixelHeight];
            image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            Require(image.PixelWidth == width && image.PixelHeight == surface.Height
                    && pixels.Count(pixel => (uint)pixel >> 24 == 255) >= pixels.Length * 0.98
                    && pixels.Take((int)backdrop.Top * image.PixelWidth).Contains(unchecked((int)0xffff7f50)),
                $"{name} must contain the opaque preview with the coral fixture element above the toolbar.");

            foreach (FrameworkElement control in Find<Grid>(preview, "PreviewToolbar").Children)
            {
                var rect = control.TransformToAncestor(preview).TransformBounds(new Rect(control.RenderSize));
                var sample = new Int32Rect((int)Math.Ceiling(rect.Left), (int)Math.Ceiling(rect.Top),
                    (int)rect.Width - 1, (int)rect.Height - 1);
                var controlPixels = new int[sample.Width * sample.Height];
                image.CopyPixels(sample, controlPixels, sample.Width * 4, 0);
                Require(controlPixels.Distinct().Take(3).Count() == 3,
                    $"{name} must visibly render the {control.GetType().Name} at {rect}.");
            }
        }
    }

    private static void Place(Canvas canvas, FrameworkElement child, double x, double y)
    {
        Canvas.SetLeft(child, x);
        Canvas.SetTop(child, y);
        canvas.Children.Add(child);
    }

    private static void VerifyProjectedPixel(FrameworkElement viewport, Rect selectionBounds, Color expected)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(viewport.ActualWidth),
            (int)Math.Ceiling(viewport.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(viewport);
        var sample = new Int32Rect((int)(selectionBounds.Left + (selectionBounds.Width / 2)),
            (int)(selectionBounds.Top + (selectionBounds.Height / 2)), 1, 1);
        var pixel = new byte[4];
        bitmap.CopyPixels(sample, pixel, pixel.Length, 0);
        var actual = Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        Require(actual == expected, $"The outline must surround the selected visual in the rendered scene. Expected {expected}, got {actual}.");
    }

    private static void RequireClose(Rect actual, Rect expected, string context)
    {
        const double tolerance = 1.5;
        Require(!actual.IsEmpty && !expected.IsEmpty && actual.Width > 0 && actual.Height > 0
                && Math.Abs(actual.Left - expected.Left) <= tolerance && Math.Abs(actual.Top - expected.Top) <= tolerance
                && Math.Abs(actual.Right - expected.Right) <= tolerance && Math.Abs(actual.Bottom - expected.Bottom) <= tolerance,
            $"{context} expected {expected}, got {actual}.");
    }

    private static T Find<T>(FrameworkElement parent, string name)
        where T : class
    {
        return parent.FindName(name) as T
            ?? throw new InvalidOperationException("Missing workbench preview highlight element " + name + ".");
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
