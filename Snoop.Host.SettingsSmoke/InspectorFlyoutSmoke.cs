namespace Snoop.Host.SettingsSmoke;

using System;
using System.IO;
using System.Linq;
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
                if (themePicker.ItemContainerGenerator.ContainerFromItem(mode) is not ComboBoxItem selection || !selection.IsSelected)
                {
                    throw new InvalidOperationException("The open theme picker lost its current selection.");
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
            }
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

        Console.WriteLine("PASS: live top menu, nested menu, selected theme list and editable filter use Fluent popups in both themes.");
    }

    private static FrameworkElement VerifyPopup(Control owner, Window inspector)
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

    private static void Capture(FrameworkElement surface, string name, params FrameworkElement[] popups)
    {
        var path = Environment.GetEnvironmentVariable("SNOOP_UI_PREVIEW_DIR");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // Popups have separate HWNDs. Composite their actual WPF renders at their screen positions.
        var layers = new[] { surface }.Concat(popups).Select(control =>
        {
            var size = new Size(Math.Ceiling(control.ActualWidth), Math.Ceiling(control.ActualHeight));
            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(control);
            var origin = surface.PointFromScreen(control.PointToScreen(default));
            return new { Bitmap = bitmap, Bounds = new Rect(origin, size) };
        }).ToArray();
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            foreach (var layer in layers)
            {
                context.DrawImage(layer.Bitmap, layer.Bounds);
            }
        }

        var image = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),
            (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory(path);
        using var file = File.Create(Path.Combine(path, name + ".png"));
        encoder.Save(file);
    }

    private static void Drain()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }
}
