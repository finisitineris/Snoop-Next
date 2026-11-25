namespace Snoop.Host.SettingsSmoke;

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Snoop.Core;
using Snoop.Localization;
using Snoop.Windows;

internal static class InspectorFlyoutSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var language = InspectorLocalization.Default.LanguageTag;
        var originalTheme = Snoop.Core.Settings.Default.ThemeMode;
        var themePicker = (ComboBox)inspector.FindName("ThemeComboBox");
        var filter = (ComboBox)inspector.FindName("filterComboBox");
        var viewMenu = (MenuItem)inspector.FindName("WorkbenchViewMenu");
        var densityMenu = (MenuItem)inspector.FindName("WorkbenchDensityMenu");
        var settingsMenu = (MenuItem)inspector.FindName("WorkbenchSettingsMenu");
        var surface = (FrameworkElement)inspector.FindName("WorkbenchChrome");
        try
        {
            InspectorLocalization.Default.SetLanguage("zh-CN");
            inspector.Show();
            inspector.WindowState = WindowState.Normal;
            inspector.Left = 12;
            inspector.Top = 12;
            inspector.Width = Math.Min(1100, SystemParameters.WorkArea.Width - 24);
            inspector.Height = Math.Min(680, SystemParameters.WorkArea.Height - 24);
            inspector.Activate();
            Drain();
            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
            {
                Snoop.Core.Settings.Default.ThemeMode = mode;
                Drain();
                viewMenu.IsSubmenuOpen = true;
                var menu = VerifyPopup(viewMenu, inspector);
                Capture(surface, "menu-" + mode, menu);
                viewMenu.IsSubmenuOpen = false;
                Drain();

                settingsMenu.IsSubmenuOpen = true;
                var parent = VerifyPopup(settingsMenu, inspector);
                densityMenu.IsSubmenuOpen = true;
                var child = VerifyPopup(densityMenu, inspector);
                Capture(surface, "submenu-" + mode, parent, child);
                densityMenu.IsSubmenuOpen = false;
                settingsMenu.IsSubmenuOpen = false;
                Drain();

                themePicker.IsDropDownOpen = true;
                var list = VerifyPopup(themePicker, inspector);
                if (themePicker.ItemContainerGenerator.ContainerFromItem(mode) is not ComboBoxItem selection || !selection.IsSelected
                    || themePicker.Template.FindName("Selection", themePicker) is not ContentPresenter selectedText
                    || VisualTreeHelper.GetChild(selectedText, 0) is not TextBlock caption
                    || caption.Text != InspectorLocalization.Default["Theme." + mode] || list.ActualWidth < 96)
                {
                    throw new InvalidOperationException("The theme picker must show a localized selection and a full-width list.");
                }

                Capture(surface, "dropdown-" + mode, list);
                themePicker.IsDropDownOpen = false;
                Drain();

                filter.ApplyTemplate();
                if (filter.Template.FindName("PART_EditableTextBox", filter) is not TextBox edit || !filter.IsEditable)
                {
                    throw new InvalidOperationException("The tree filter lost its editable text part.");
                }

                edit.Focus();
                filter.IsDropDownOpen = true;
                VerifyPopup(filter, inspector);
                filter.IsDropDownOpen = false;
                Drain();
                InspectorContextMenuSmoke.Verify(inspector);
                TreeNodeMenuSmoke.Verify(inspector, mode);
            }

            VerifyEditableFilterContrast(inspector, filter);
        }
        finally
        {
            viewMenu.IsSubmenuOpen = false;
            densityMenu.IsSubmenuOpen = false;
            settingsMenu.IsSubmenuOpen = false;
            themePicker.IsDropDownOpen = false;
            filter.IsDropDownOpen = false;
            InspectorLocalization.Default.SetLanguage(language);
            Snoop.Core.Settings.Default.ThemeMode = originalTheme;
        }

        Console.WriteLine("PASS: live top menu, nested menu, selected theme list, editable filter, property context menu and grouped event picker use Fluent popups in both themes.");
    }

    private static void VerifyEditableFilterContrast(SnoopUI inspector, ComboBox filter)
    {
        ThemeManager.Current.ApplyTheme(ThemeMode.Light, inspector);
        var palette = new ResourceDictionary
        {
            Source = new Uri("/Snoop.Core;component/Themes/Colors.HighContrast.xaml", UriKind.Relative)
        };
        palette[SystemColors.WindowColorKey] = Colors.Black;
        palette[SystemColors.WindowTextColorKey] = Colors.White;
        inspector.Resources.MergedDictionaries.Add(palette);
        try
        {
            Drain();
            filter.ApplyTemplate();
            var edit = (TextBox)filter.Template.FindName("PART_EditableTextBox", filter);
            var button = (ToggleButton)filter.Template.FindName("DropButton", filter);
            button.ApplyTemplate();
            var field = (Border)button.Template.FindName("Field", button);
            if (edit.Visibility != Visibility.Visible || edit.Background is not SolidColorBrush transparent || transparent.Color.A != 0
                || edit.Foreground is not SolidColorBrush text || text.Color != Colors.White
                || field.Background is not SolidColorBrush background || background.Color != Colors.Black)
            {
                throw new InvalidOperationException($"Expected white editable filter text on a black field. Text {edit.Foreground}, editor {edit.Background}, field {field.Background}.");
            }

            foreach (var state in new[] { UIElement.IsMouseOverProperty, ButtonBase.IsPressedProperty })
            {
                var setter = button.Template.Triggers.OfType<Trigger>()
                    .Single(trigger => trigger.Property == state && Equals(trigger.Value, true))
                    .Setters.OfType<Setter>().Single(item => item.TargetName == "Field" && item.Property == Border.BackgroundProperty);
                if (setter.Value is not DynamicResourceExtension reference
                    || field.TryFindResource(reference.ResourceKey) is not SolidColorBrush fill || fill.Color != Colors.Black)
                {
                    throw new InvalidOperationException("The editable filter toggle must keep its contrast field color for " + state.Name);
                }
            }
        }
        finally
        {
            inspector.Resources.MergedDictionaries.Remove(palette);
            ThemeManager.Current.ApplyTheme(Snoop.Core.Settings.Default.ThemeMode, inspector);
        }

        Console.WriteLine("PASS: editable tree filter uses contrast text and field colors across its idle, hover and pressed template states.");
    }

    internal static FrameworkElement VerifyPopup(Control owner, Window inspector)
    {
        Drain();
        owner.ApplyTemplate();
        if (owner.Template.FindName("PART_Popup", owner) is not Popup { IsOpen: true, Child: FrameworkElement root }
            || PresentationSource.FromVisual(root) is not HwndSource popupSource
            || popupSource.Handle == new WindowInteropHelper(inspector).Handle
            || owner.Template.FindName("FlyoutSurface", owner) is not Border panel
            || panel.Background is not SolidColorBrush fill
            || panel.ActualHeight <= 0 || panel.ActualWidth <= 0 || panel.CornerRadius.TopLeft < 4
            || fill.Color != ((SolidColorBrush)inspector.FindResource("SnoopNext.Brush.Panel")).Color)
        {
            throw new InvalidOperationException($"Missing or unthemed live popup for {owner.Name} ({owner.GetType().Name}).");
        }

        return root;
    }

    internal static void Capture(FrameworkElement surface, string name, params FrameworkElement[] popups)
    {
        var window = Window.GetWindow(surface);
        var hwnd = new WindowInteropHelper(window).Handle;
        VerifyCaptionColor(hwnd, window);
        var path = Environment.GetEnvironmentVariable("SNOOP_UI_PREVIEW_DIR");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // PointToScreen and GetWindowRect share screen pixel coordinates. Each popup
        // has its own presentation source and can be on a differently scaled monitor.
        var frame = CaptureNativeFrame(hwnd, out var bounds);
        var layers = new[] { surface }.Concat(popups).Select(control =>
        {
            var scale = PresentationSource.FromVisual(control).CompositionTarget.TransformToDevice;
            var width = (int)Math.Ceiling(control.ActualWidth * scale.M11);
            var height = (int)Math.Ceiling(control.ActualHeight * scale.M22);
            var bitmap = new RenderTargetBitmap(width, height, 96 * scale.M11, 96 * scale.M22, PixelFormats.Pbgra32);
            bitmap.Render(control);
            return new { Bitmap = bitmap, Bounds = new Rect(control.PointToScreen(default), new Size(width, height)) };
        }).ToArray();
        var frameBounds = bounds;
        foreach (var layer in layers)
        {
            bounds.Union(layer.Bounds);
        }

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            if (frame is not null)
            {
                context.DrawImage(frame, frameBounds);
            }

            foreach (var layer in layers)
            {
                context.DrawImage(layer.Bitmap, layer.Bounds);
            }

            context.Pop();
        }

        var image = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width),
            (int)Math.Ceiling(bounds.Height), 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(path);
        using var file = File.Create(Path.Combine(path, name + ".png"));
        encoder.Save(file);
    }

    private static BitmapSource? CaptureNativeFrame(IntPtr hwnd, out Rect bounds)
    {
        bounds = Rect.Empty;
        if (!GetWindowRect(hwnd, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            var dc = graphics.GetHdc();
            try
            {
                // PW_RENDERFULLCONTENT without PW_CLIENTONLY includes the native caption.
                if (!PrintWindow(hwnd, dc, 2))
                {
                    return null;
                }
            }
            finally
            {
                graphics.ReleaseHdc(dc);
            }
        }

        var pixels = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
        try
        {
            var frame = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null,
                pixels.Scan0, pixels.Stride * height, pixels.Stride);
            frame.Freeze();
            bounds = new Rect(rect.Left, rect.Top, width, height);
            return frame;
        }
        finally
        {
            bitmap.UnlockBits(pixels);
        }
    }

    private static void VerifyCaptionColor(IntPtr hwnd, Window window)
    {
        // DWMWA_CAPTION_COLOR is available starting with Windows 11 build 22000.
        if (DwmGetWindowAttribute(hwnd, 35, out var actual, sizeof(int)) != 0)
        {
            return;
        }

        var color = ((SolidColorBrush)window.FindResource("SnoopNext.Brush.Background")).Color;
        var expected = SystemParameters.HighContrast ? -1 : color.R | (color.G << 8) | (color.B << 16);
        if (actual != expected)
        {
            throw new InvalidOperationException($"Native inspector caption expected COLORREF {expected:X8}; got {actual:X8}.");
        }
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr deviceContext, uint flags);

    private static void Drain()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
