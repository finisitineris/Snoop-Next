namespace Snoop.Windows;

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Dwm;
using Snoop.Core;
using Snoop.Infrastructure;
using Snoop.Infrastructure.Helpers;

public class SnoopBaseWindow : Window
{
    public SnoopBaseWindow()
    {
        this.UseLayoutRounding = true;
        this.InheritanceBehavior = InheritanceBehavior.SkipToThemeNext;
        this.SnapsToDevicePixels = true;
        this.Icon = new BitmapImage(new Uri("pack://application:,,,/Snoop.Core;component/Snoop.ico"));

        SnoopPartsRegistry.AddSnoopVisualTreeRoot(this);

        this.SetValue(DefaultStyleKeyFixer.KeyFixerProperty, new object());
    }

    protected override void OnInitialized(EventArgs e)
    {
        {
            var resourceDictionary = (ResourceDictionary)Application.LoadComponent(new Uri("/Snoop.Core;component/Icons.xaml", UriKind.Relative));
            Debug.Assert(resourceDictionary is not null, "Icons could not be loaded.");
            this.Resources.MergedDictionaries.Add(resourceDictionary);
        }

        {
            var resourceDictionary = (ResourceDictionary)Application.LoadComponent(new Uri("/Snoop.Core;component/Styles.xaml", UriKind.Relative));
            Debug.Assert(resourceDictionary is not null, "Styles could not be loaded.");
            this.Resources.MergedDictionaries.Add(resourceDictionary);
        }

        ThemeManager.Current.ApplyTheme(Settings.Default.ThemeMode, this);

        this.Style = (Style)this.FindResource(typeof(Window));

        base.OnInitialized(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        this.UpdateNativeFrame();
    }

    internal unsafe void UpdateNativeFrame()
    {
        var hwnd = (HWND)new WindowInteropHelper(this).Handle;
        if (hwnd == HWND.Null
            || this.TryFindResource("SnoopNext.Brush.Background") is not SolidColorBrush background
            || this.TryFindResource("SnoopNext.Brush.Text") is not SolidColorBrush foreground)
        {
            return;
        }

        // Keep the native caption, system menu, snap layouts and resize hit-testing.
        // DWM ignores unsupported individual attributes on earlier Windows versions.
        var contrast = SystemParameters.HighContrast;
        Set(DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, !contrast && background.Color.R < 128 ? 1 : 0);
        Set(DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, contrast ? -1 : ColorRef(background.Color));
        Set(DWMWINDOWATTRIBUTE.DWMWA_TEXT_COLOR, contrast ? -1 : ColorRef(foreground.Color));
        Set(DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, 2); // DWMWCP_ROUND

        void Set(DWMWINDOWATTRIBUTE attribute, int value)
            => _ = PInvoke.DwmSetWindowAttribute(hwnd, attribute, &value, sizeof(int));

        static int ColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);
    }

    protected override void OnClosed(EventArgs e)
    {
        SnoopPartsRegistry.RemoveSnoopVisualTreeRoot(this);

        base.OnClosed(e);
    }
}