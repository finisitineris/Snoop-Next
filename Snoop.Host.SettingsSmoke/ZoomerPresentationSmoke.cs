namespace Snoop.Host.SettingsSmoke;

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Core;
using Snoop.Localization;
using Snoop.Windows;
using Rectangle = System.Windows.Shapes.Rectangle;

internal static class ZoomerPresentationSmoke
{
    public static void Verify()
    {
        var language = InspectorLocalization.Default.LanguageTag;
        var originalPlacement = Snoop.Core.Settings.Default.ZoomerWindowPlacement;
        var content = MakeContent();
        var target = new Window { Content = content, Width = 440, Height = 240, ShowInTaskbar = false };
        var originalParent = content.Parent;
        var viewer = new Zoomer();
        try
        {
            InspectorLocalization.Default.SetLanguage("zh-CN");
            viewer.Target = target;
            var surface = (Grid)viewer.FindName("ViewerSurface");
            var viewbox = (Viewbox)viewer.FindName("Viewbox");
            var empty = (Grid)viewer.FindName("EmptyState");
            var mode2D = (RadioButton)viewer.FindName("Mode2D");
            var mode3D = (RadioButton)viewer.FindName("Mode3D");
            var options = (Border)viewer.FindName("ThreeDViewControls");
            target.Show();
            viewer.Show();
            viewer.WindowState = WindowState.Normal;
            viewer.Width = 680;
            viewer.Height = 430;
            viewer.Left = 18;
            viewer.Top = 18;
            ThemeManager.Current.ApplyTheme(ThemeMode.Light, viewer);
            Drain();
            if (viewbox.Child is not Rectangle { Fill: VisualBrush layer }
                || !ReferenceEquals(layer.Visual, VisualTreeHelper.GetChild(target, 0))
                || empty.IsVisible || mode2D.IsChecked != true)
            {
                throw new InvalidOperationException("A newly opened WPF Window must acquire a live 2D client projection after layout.");
            }

            VerifyToolbar(viewer, capture: false);
            Capture(surface, "zoomer-2D");
            Zoomer.SwitchTo3DCommand.Execute(null, viewer);
            Drain();
            VerifyToolbar(viewer, capture: true);
            ThemeManager.Current.ApplyTheme(ThemeMode.Dark, viewer);
            Drain();
            if (viewbox.Child is not VisualTree3DView { Camera: not null } scene || scene.Children.Count == 0
                || !options.IsVisible || mode3D.IsChecked != true || empty.IsVisible)
            {
                throw new InvalidOperationException("The 3D command must display the textured scene and its controls.");
            }

            var initialCameraPosition = ((ProjectionCamera)scene.Camera).Position;
            var spacing = (Slider)viewer.FindName("ZScaleSlider");
            var dpi = (ComboBox)viewer.FindName("dpiBox");
            spacing.Value = 0.9;
            ((Slider)viewer.FindName("ColorSlider")).Value = .05;
            dpi.SelectedIndex = 1;
            Drain();
            if (viewbox.Child is not VisualTree3DView revised || ReferenceEquals(scene, revised)
                || Math.Abs(revised.ZScale - Math.Pow(10, 0.9)) > .01)
            {
                throw new InvalidOperationException("Changing texture DPI must rebuild the 3D view at the selected layer spacing.");
            }

            Capture(surface, "zoomer-3D");
            VerifyFit(revised, (Grid)viewer.FindName("DocumentRoot"), wide: true);
            Zoomer.ResetCommand.Execute(null, viewer);
            Drain();
            if (revised.ZScale != 1 || (((ProjectionCamera)revised.Camera).Position - initialCameraPosition).Length > .1)
            {
                throw new InvalidOperationException("Fitting after a DPI change must restore the default camera and layer spacing.");
            }

            target.Width = 212;
            target.Height = 460;
            Drain();
            viewer.Target = target;
            Drain();
            VerifyFit((VisualTree3DView)viewbox.Child, (Grid)viewer.FindName("DocumentRoot"), wide: false);
            Zoomer.SwitchTo2DCommand.Execute(null, viewer);
            if (mode2D.IsChecked != true || mode3D.IsChecked != false || options.IsVisible)
            {
                throw new InvalidOperationException("The 2D command must restore its selected segment and hide the 3D options.");
            }

            viewer.Target = new object();
            Drain();
            if (viewbox.Child is not null || !empty.IsVisible || Zoomer.SwitchTo3DCommand.CanExecute(null, viewer))
            {
                throw new InvalidOperationException("Nonvisual selections must show an empty-state message and disable 3D.");
            }

            if (!ReferenceEquals(content.Parent, originalParent) || !ReferenceEquals(target.Content, content))
            {
                throw new InvalidOperationException("The magnifier changed the inspected window's logical content parent.");
            }
        }
        finally
        {
            viewer.Close();
            target.Close();
            Snoop.Core.Settings.Default.ZoomerWindowPlacement = originalPlacement;
            InspectorLocalization.Default.SetLanguage(language);
        }

        Console.WriteLine("PASS: zoomer paints target pixels in 2D and textured 3D, syncs segments and sliders, handles empty targets.");
    }

    private static void VerifyToolbar(Zoomer viewer, bool capture)
    {
        var toolbar = (Border)viewer.FindName("ToolbarBorder");
        var group = (Border)viewer.FindName("ModeGroup");
        var mode2D = (RadioButton)viewer.FindName("Mode2D");
        var mode3D = (RadioButton)viewer.FindName("Mode3D");
        var zoomOut = (Button)viewer.FindName("ZoomOutButton");
        var zoomIn = (Button)viewer.FindName("ZoomInButton");
        var fit = (Button)viewer.FindName("FitButton");
        var label = (Label)viewer.FindName("BackdropLabel");
        var slider = (Slider)viewer.FindName("ColorSlider");
        var width = viewer.Width;
        var language = InspectorLocalization.Default.LanguageTag;
        try
        {
            Check(wrapped: false);
            InspectorLocalization.Default.SetLanguage("en-US");
            viewer.Width = viewer.MinWidth;
            Drain();
            Check(wrapped: true);
        }
        finally
        {
            viewer.Width = width;
            InspectorLocalization.Default.SetLanguage(language);
            Drain();
        }

        void Check(bool wrapped)
        {
            var frame = new Rect(toolbar.RenderSize);
            var controls = new FrameworkElement[] { group, zoomOut, zoomIn, fit, label, slider };
            for (var i = 0; i < controls.Length; i++)
            {
                var bounds = Bounds(controls[i]);
                if (!frame.Contains(bounds) || bounds.Width <= 0 || bounds.Height <= 0)
                {
                    throw new InvalidOperationException($"Clipped or empty zoom toolbar control {controls[i].Name} at {bounds} in {frame}.");
                }

                for (var j = 0; j < i; j++)
                {
                    if (Rect.Intersect(bounds, Bounds(controls[j])) is { Width: > 0, Height: > 0 })
                    {
                        throw new InvalidOperationException($"Zoom toolbar controls {controls[j].Name} and {controls[i].Name} overlap.");
                    }
                }
            }

            foreach (var control in new FrameworkElement[] { group, zoomOut, zoomIn, fit })
            {
                Close(control.ActualHeight, 32, control.Name + " height");
                Close(Bounds(control).Top, Bounds(group).Top, control.Name + " top edge");
            }

            Close(zoomOut.ActualWidth, 32, "zoom-out width");
            Close(zoomIn.ActualWidth, 32, "zoom-in width");
            Close(mode2D.ActualWidth, mode3D.ActualWidth, "segment widths");
            Close(mode2D.ActualHeight, mode3D.ActualHeight, "segment heights");
            Close(mode2D.FontSize, fit.FontSize, "mode and fit font sizes");
            Close(label.FontSize, fit.FontSize, "label and fit font sizes");

            // The Label's template contains a Border and a content presenter. Inspect the
            // presenter itself so a top-aligned caption in a centered label is caught.
            var caption = (FrameworkElement)VisualTreeHelper.GetChild(VisualTreeHelper.GetChild(label, 0), 0);
            var sliderMidpoint = Bounds(slider).Top + (slider.ActualHeight / 2);
            Close(Bounds(caption).Top + (caption.ActualHeight / 2), sliderMidpoint, "backdrop caption and slider center");
            if (wrapped)
            {
                if (Bounds(slider).Top < Bounds(group).Bottom)
                {
                    throw new InvalidOperationException("The backdrop group must wrap below the mode group in a narrow window.");
                }
            }
            else
            {
                Close(sliderMidpoint, Bounds(group).Top + (group.ActualHeight / 2), "backdrop and command row center");
            }

            if (capture)
            {
                LauncherAppearanceSmoke.CaptureIfRequested(toolbar, wrapped ? "zoomer-toolbar-narrow" : "zoomer-toolbar");
            }
        }

        Rect Bounds(FrameworkElement control) => control.TransformToAncestor(toolbar).TransformBounds(new Rect(control.RenderSize));

        void Close(double actual, double expected, string measurement)
        {
            if (Math.Abs(actual - expected) > 1)
            {
                throw new InvalidOperationException($"Unexpected zoom toolbar {measurement}: expected {expected} DIP, got {actual} DIP.");
            }
        }
    }

    private static void VerifyFit(VisualTree3DView scene, Grid canvas, bool wide)
    {
        var model = (ModelVisual3D)scene.Children[0];
        var bounds = model.Content.Bounds;
        var projection = model.TransformToAncestor(canvas);
        var projected = Rect.Empty;
        foreach (var x in new[] { bounds.X, bounds.X + bounds.SizeX })
        {
            foreach (var y in new[] { bounds.Y, bounds.Y + bounds.SizeY })
            {
                foreach (var z in new[] { bounds.Z, bounds.Z + bounds.SizeZ })
                {
                    if (!projection.TryTransform(new Point3D(x, y, z), out var point))
                    {
                        throw new InvalidOperationException("The 3D bounds are behind the fit camera.");
                    }

                    projected.Union(point);
                }
            }
        }

        var available = new Rect(canvas.RenderSize);
        if (!available.Contains(projected)
            || (wide && projected.Width < available.Width * 0.55)
            || (!wide && projected.Height < available.Height * 0.7))
        {
            throw new InvalidOperationException($"The 3D volume {projected} must fit and fill its canvas {available} (wide: {wide}).");
        }
    }

    private static Grid MakeContent()
    {
        var grid = new Grid { Background = Brushes.AliceBlue };
        var rows = new StackPanel();
        grid.Children.Add(new Border
        {
            Margin = new Thickness(12), Padding = new Thickness(16, 10, 16, 8), Background = Brushes.White,
            BorderBrush = Brushes.LightSteelBlue, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = rows
        });
        rows.Children.Add(new TextBlock { Text = "渲染统计", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.MidnightBlue });
        AddRow("布局", 0.32, Brushes.DarkCyan);
        AddRow("绘制", 0.78, Brushes.DarkCyan);
        AddRow("合成", 0.55, Brushes.SteelBlue);
        return grid;

        void AddRow(string label, double fraction, Brush color)
        {
            var row = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            rows.Children.Add(row);
            row.Children.Add(new TextBlock { Text = label, Foreground = Brushes.DarkSlateGray, Width = 48 });
            var bar = new Grid { Height = 10, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.AliceBlue };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
            bar.Children.Add(new Border { Background = color });
            row.Children.Add(bar);
        }
    }

    private static void Capture(FrameworkElement surface, string name)
    {
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);

        // The two teal bars in the fixture are the only visual with both channels in this range.
        var painted = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 2] < 20 && pixels[i + 1] is > 100 and < 185 && Math.Abs(pixels[i] - pixels[i + 1]) < 5)
            {
                painted++;
            }
        }

        if (painted < 150)
        {
            throw new InvalidOperationException($"{name} must contain the target's rendered teal bars, found {painted} pixels.");
        }

        var directory = Environment.GetEnvironmentVariable("SNOOP_UI_PREVIEW_DIR");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name + ".png"));
            encoder.Save(stream);
        }
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
}
