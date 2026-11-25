namespace Snoop.Host.SettingsSmoke;

using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snoop.Core;
using Snoop.Localization;
using Snoop.Next.Localization;
using Snoop.Views;
using Snoop.Windows;

internal static class LauncherAppearanceSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        // Lay out the real launcher content off-screen. Showing the window would install
        // the global keyboard hook. Opening the combo would enumerate desktop processes.
        var chooser = new AppChooser();
        var content = (FrameworkElement)chooser.Content;
        var language = LocalizationManager.Default.LanguageTag;
        var inspectorLanguage = InspectorLocalization.Default.LanguageTag;
        var palette = Snoop.Core.Settings.Default.ThemeMode;
        try
        {
            chooser.ApplyTemplate();
            WorkbenchLayoutSmoke.Arrange(content, new Rect(0, 0, chooser.Width, chooser.Height));
            var list = Find<ComboBox>("windowsComboBox");
            var attach = Find<Button>("InspectTargetButton");
            var magnify = Find<Button>("MagnifyTargetButton");
            if (!chooser.Topmost || !ReferenceEquals(list.ItemsSource, chooser.WindowInfos)
                || attach.Command != AppChooser.InspectCommand || magnify.Command != AppChooser.MagnifyCommand
                || Find<Button>("RefreshTargetsButton").Command != AppChooser.RefreshCommand
                || Find<WindowFinder>("InspectWindowFinder").WindowFinderType != WindowFinderType.Snoop
                || Find<WindowFinder>("MagnifyWindowFinder").WindowFinderType != WindowFinderType.Magnify)
            {
                throw new InvalidOperationException("Launcher commands and both drag targets must remain independently available.");
            }

            if (AppChooser.InspectCommand.CanExecute(null, chooser) || AppChooser.MagnifyCommand.CanExecute(null, chooser)
                || !AppChooser.RefreshCommand.InputGestures.OfType<KeyGesture>().Any(key => key.Key == Key.F5 && key.Modifiers == ModifierKeys.None))
            {
                throw new InvalidOperationException("The empty launcher must support F5 and disable target-dependent commands.");
            }

            foreach (var tag in new[] { "zh-CN", "en-US" })
            {
                LocalizationManager.Default.SetLanguage(tag);
                foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
                {
                    ThemeManager.Current.ApplyTheme(mode, chooser);
                    var bounds = new Rect(0, 0, chooser.MinWidth, chooser.Height);
                    WorkbenchLayoutSmoke.Arrange(content, bounds);
                    foreach (var name in new[]
                    {
                        "windowsComboBox", "InspectTargetButton", "MagnifyTargetButton",
                        "InspectWindowFinder", "MagnifyWindowFinder", "CloseButton"
                    })
                    {
                        var element = Find<FrameworkElement>(name);
                        var box = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
                        if (box.Width <= 0 || box.Height <= 0 || !bounds.Contains(box))
                        {
                            throw new InvalidOperationException($"Launcher control {name} was clipped in {tag}/{mode} at {box}.");
                        }
                    }

                    if (AutomationProperties.GetName(attach) != LocalizationManager.Default["Chooser.Inspect"])
                    {
                        throw new InvalidOperationException("The attach action needs an accessible name.");
                    }

                    if (tag == "zh-CN")
                    {
                        WorkbenchLayoutSmoke.Arrange(content, new Rect(0, 0, chooser.Width, chooser.Height));
                        CaptureIfRequested(content, "launcher-" + mode);
                    }
                }
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNOOP_UI_PREVIEW_DIR")))
            {
                var settings = new SnoopBaseWindow();
                settings.BeginInit();
                settings.Content = new SettingsView();
                settings.EndInit();
                try
                {
                    LocalizationManager.Default.SetLanguage("zh-CN");
                    InspectorLocalization.Default.SetLanguage("zh-CN");
                    foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
                    {
                        ThemeManager.Current.ApplyTheme(mode, inspector);
                        ThemeManager.Current.ApplyTheme(mode, settings);
                        var chrome = (FrameworkElement)inspector.FindName("WorkbenchChrome");
                        WorkbenchLayoutSmoke.Arrange(chrome, new Rect(0, 0, 1280, 720));
                        CaptureIfRequested(chrome, "inspector-" + mode);
                        var view = (FrameworkElement)settings.Content;
                        WorkbenchLayoutSmoke.Arrange(view, new Rect(0, 0, 560, 400));
                        CaptureIfRequested(view, "settings-" + mode);
                    }
                }
                finally
                {
                    settings.Close();
                }
            }
        }
        finally
        {
            LocalizationManager.Default.SetLanguage(language);
            InspectorLocalization.Default.SetLanguage(inspectorLanguage);
            ThemeManager.Current.ApplyTheme(palette, inspector);
            chooser.Close();
        }

        Console.WriteLine("PASS: launcher command routing, independent pickers, light/dark layout in Chinese and English");

        T Find<T>(string name)
            where T : FrameworkElement
            => chooser.FindName(name) as T ?? throw new InvalidOperationException("Missing launcher element " + name);
    }

    internal static void CaptureIfRequested(FrameworkElement surface, string name)
    {
        var directory = Environment.GetEnvironmentVariable("SNOOP_UI_PREVIEW_DIR");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),
            (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
