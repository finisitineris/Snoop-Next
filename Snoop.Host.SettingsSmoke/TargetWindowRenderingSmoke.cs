namespace Snoop.Host.SettingsSmoke;

using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Windows;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;

internal static class TargetWindowRenderingSmoke
{
    public static void Verify(Application app)
    {
        // A target inheriting Snoop's DataContext or using its resource scope loses the left swatch.
        var hostPaintKey = new object();
        var swatchStyle = new Style(typeof(Border));
        swatchStyle.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.White));
        var hostContext = new DataTrigger
        {
            Binding = new Binding(nameof(FrameworkElement.DataContext))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1),
            },
            Value = null,
        };
        hostContext.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(hostPaintKey)));
        swatchStyle.Triggers.Add(hostContext);
        var left = new Border { Style = swatchStyle };
        var right = new Border { Background = Brushes.Coral };
        var client = new Grid();
        client.ColumnDefinitions.Add(new ColumnDefinition());
        client.ColumnDefinitions.Add(new ColumnDefinition());
        client.Children.Add(left);
        Grid.SetColumn(right, 1);
        client.Children.Add(right);
        var target = new Window
        {
            Title = "Snoop target rendering fixture",
            Content = client,
            Width = 260,
            Height = 160,
            Left = 30,
            Top = 40,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
        };

        var originalMainWindow = app.MainWindow;
        SnoopUI? inspector = null;
        try
        {
            app.Resources.Add(hostPaintKey, Brushes.DarkCyan);
            app.MainWindow = target;
            target.Show();
            VerifyWindowPixels(target, Colors.Coral, "before inspection");

            // This is the managed part of SnoopManager.CreateSnoopWindow's normal path:
            // inspect the application on its Dispatcher, then select the requested Window.
            inspector = new SnoopUI();
            inspector.Resources.Add(hostPaintKey, Brushes.White);
            inspector.Inspect(app);
            var preview = (Previewer)inspector.FindName("PreviewArea");
            preview.IsActive = true;
            var zoomer = (ZoomerControl)preview.FindName("Zoomer");
            var projection = (Viewbox)zoomer.FindName("Viewbox");

            SelectAndRepaint(target, Brushes.MediumPurple, "window selected");
            SelectAndRepaint(right, Brushes.Gold, "child selected");
            SelectAndRepaint(target, Brushes.SeaGreen, "window reselected");
            inspector.Close();
            inspector = null;
            right.Background = Brushes.SlateBlue;
            VerifyWindowPixels(target, Colors.SlateBlue, "after inspector closed");

            void SelectAndRepaint(FrameworkElement selected, SolidColorBrush color, string stage)
            {
                inspector!.Target = selected;
                // Force a new native frame. A cached image from before inspection must fail.
                right.Background = color;
                VerifyWindowPixels(target, color.Color, stage);
                if (target.DataContext is not null || !ReferenceEquals(target.TryFindResource(hostPaintKey), Brushes.DarkCyan))
                {
                    throw new InvalidOperationException($"Inspection replaced the host window's DataContext or resource scope ({stage}).");
                }

                if (!ReferenceEquals(inspector.Target, selected) || !ReferenceEquals(preview.Target, selected)
                    || projection.Child is not Rectangle { Fill: VisualBrush brush }
                    || !ReferenceEquals(brush.Visual, VisualTreeHelper.GetChild(target, 0)))
                {
                    throw new InvalidOperationException($"The workbench must project the window's client visual and retain the selected object ({stage}).");
                }
            }
        }
        finally
        {
            inspector?.Close();
            app.MainWindow = originalMainWindow;
            target.Close();
            app.Resources.Remove(hostPaintKey);
        }

        Console.WriteLine("PASS: real target HWND repainted both swatches before inspection, during window/child selection, and after close");
    }

    private static void VerifyWindowPixels(Window target, Color expectedRight, string stage)
    {
        var hwnd = new WindowInteropHelper(target).Handle;
        var source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("The regression fixture needs a live target HWND.");
        var scale = source.CompositionTarget.TransformToDevice;
        var width = (int)Math.Round(target.ActualWidth * scale.M11);
        var height = (int)Math.Round(target.ActualHeight * scale.M22);
        using var frame = new Bitmap(width, height);
        var timer = Stopwatch.StartNew();
        System.Drawing.Color left;
        System.Drawing.Color right;
        do
        {
            target.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            using (var graphics = Graphics.FromImage(frame))
            {
                var dc = graphics.GetHdc();
                try
                {
                    // PW_CLIENTONLY | PW_RENDERFULLCONTENT. This reads the target HWND
                    // independently of both the inspector's VisualBrush and desktop occlusion.
                    if (!PrintWindow(hwnd, dc, 3))
                    {
                        throw new InvalidOperationException($"PrintWindow could not capture the target client ({stage}).");
                    }
                }
                finally
                {
                    graphics.ReleaseHdc(dc);
                }
            }

            left = frame.GetPixel(width / 4, height / 2);
            right = frame.GetPixel(3 * width / 4, height / 2);
            if (Matches(left, Colors.DarkCyan) && Matches(right, expectedRight))
            {
                Console.WriteLine($"Target HWND paint OK ({stage}): {left} / {right}");
                return;
            }

            // Composition runs on a different thread, even after WPF layout is idle.
            Thread.Sleep(30);
        }
        while (timer.Elapsed < TimeSpan.FromSeconds(4));

        throw new InvalidOperationException($"Target HWND pixels changed or stopped repainting ({stage}). Expected {Colors.DarkCyan} / {expectedRight}, got {left} / {right}.");
    }

    private static bool Matches(System.Drawing.Color sample, Color expected)
    {
        return Math.Abs(sample.R - expected.R) <= 2
               && Math.Abs(sample.G - expected.G) <= 2
               && Math.Abs(sample.B - expected.B) <= 2;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);
}
