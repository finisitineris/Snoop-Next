namespace Snoop.Host.SettingsSmoke;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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
        // the global keyboard hook.
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
                VerifyLauncherPeers(chooser, list);
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

                    VerifyCompactLayout(chooser, capture: tag == "zh-CN" ? "launcher-compact-" + mode : null);

                    if (tag == "zh-CN")
                    {
                        WorkbenchLayoutSmoke.Arrange(content, new Rect(0, 0, chooser.Width, chooser.Height));
                        CaptureIfRequested(content, "launcher-" + mode);
                    }
                }
            }

            LauncherSearchSmoke.Verify(chooser, list);
            var reopened = new AppChooser { Width = 320, Height = 86 };
            try
            {
                VerifyCompactLayout(reopened, capture: null);
            }
            finally
            {
                reopened.Close();
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNOOP_UI_PREVIEW_DIR")))
            {
                LocalizationManager.Default.SetLanguage("zh-CN");
                InspectorLocalization.Default.SetLanguage("zh-CN");
                foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
                {
                    ThemeManager.Current.ApplyTheme(mode, inspector);
                    var chrome = (FrameworkElement)inspector.FindName("WorkbenchChrome");
                    WorkbenchLayoutSmoke.Arrange(chrome, new Rect(0, 0, 1280, 720));
                    CaptureIfRequested(chrome, "inspector-" + mode);
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

        Console.WriteLine("PASS: launcher commands and pickers, full 320 DIP layout, wide restore and reopen in Chinese and English");

        T Find<T>(string name)
            where T : FrameworkElement
            => chooser.FindName(name) as T ?? throw new InvalidOperationException("Missing launcher element " + name);
    }

    private static void VerifyLauncherPeers(AppChooser chooser, ComboBox list)
    {
        VerifyButton("RefreshTargetsButton", "Chooser.Refresh");
        VerifyButton("InspectTargetButton", "Chooser.Inspect");
        VerifyButton("MagnifyTargetButton", "Chooser.Magnify");
        VerifyButton("SettingsButton", "Chooser.Settings");
        VerifyButton("MinimizeButton", "Chooser.Minimize");
        VerifyButton("CloseButton", "Chooser.Close");

        void VerifyButton(string id, string key)
        {
            var button = (Button)chooser.FindName(id);
            var peer = UIElementAutomationPeer.CreatePeerForElement(button);
            if (peer.GetAutomationId() != id || peer.GetName() != LocalizationManager.Default[key]
                || peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider)
            {
                throw new InvalidOperationException($"Missing launcher button ID, localized name or Invoke pattern for {id}.");
            }
        }

        var selector = UIElementAutomationPeer.CreatePeerForElement(list);
        if (selector.GetAutomationId() != "windowsComboBox" || selector.GetName() != LocalizationManager.Default["Chooser.SelectTarget"]
            || selector.GetPattern(PatternInterface.ExpandCollapse) is not IExpandCollapseProvider
            || selector.GetPattern(PatternInterface.Selection) is not ISelectionProvider
            || !list.Focusable || !list.IsTabStop || TextSearch.GetTextPath(list) != nameof(WindowInfo.ProcessName))
        {
            throw new InvalidOperationException("The keyboard-searchable target list must expose its localized name, ID, selection and expansion.");
        }
    }

    private static void VerifyCompactLayout(AppChooser chooser, string? capture)
    {
        var applyWorkArea = typeof(AppChooser).GetMethod("ApplyWorkArea", BindingFlags.Instance | BindingFlags.NonPublic)
                            ?? throw new InvalidOperationException("Missing launcher work-area layout.");
        var content = (FrameworkElement)chooser.Content;
        var actions = (WrapPanel)chooser.FindName("TargetActions");
        var small = new Rect(48, 72, 320, 180);
        applyWorkArea.Invoke(chooser, new object[] { small, small.BottomRight });
        var bounds = new Rect(0, 0, chooser.Width, chooser.Height);
        WorkbenchLayoutSmoke.Arrange(content, bounds);
        if (bounds.Width != 320 || bounds.Height <= 48 || !small.Contains(new Rect(chooser.Left, chooser.Top, bounds.Width, bounds.Height))
                                || Grid.GetRow(actions) != 1)
        {
            throw new InvalidOperationException($"The compact launcher must reflow within its 320 DIP work area, got {bounds}.");
        }

        var names = new[]
        {
            "windowsComboBox", "RefreshTargetsButton", "InspectTargetButton", "InspectWindowFinder", "MagnifyTargetButton",
            "MagnifyWindowFinder", "SettingsButton", "MinimizeButton", "CloseButton"
        };
        var boxes = names.Select(name =>
        {
            var element = (FrameworkElement)chooser.FindName(name);
            return element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
        }).ToArray();
        for (var i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];
            if (box.Width < (i == 0 ? 145 : 28) || box.Height < 28 || !bounds.Contains(box)
                || boxes.Take(i).Any(other => Rect.Intersect(box, other) is { Width: > 0, Height: > 0 }))
            {
                throw new InvalidOperationException($"The compact launcher must expose its selector and every action without overlap. {names[i]} at {box}.");
            }
        }

        if (capture is not null)
        {
            CaptureIfRequested(content, capture);
        }

        applyWorkArea.Invoke(chooser, new object[] { new Rect(0, 0, 1280, 720), new Point(20, 20) });
        WorkbenchLayoutSmoke.Arrange(content, new Rect(0, 0, chooser.Width, chooser.Height));
        if (chooser.Width != 740 || chooser.MinWidth != 560 || chooser.Height != 48 || Grid.GetRow(actions) != 0
            || ((TextBlock)chooser.FindName("BrandLabel")).Visibility != Visibility.Visible)
        {
            throw new InvalidOperationException("A wider work area must restore the launcher's single-row geometry.");
        }
    }

    internal static void CaptureIfRequested(FrameworkElement surface, string name, FrameworkElement? region = null)
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
        BitmapSource capture = image;
        if (region is not null)
        {
            var bounds = region.TransformToAncestor(surface).TransformBounds(new Rect(region.RenderSize));
            var left = (int)Math.Floor(bounds.Left);
            var top = (int)Math.Floor(bounds.Top);
            capture = new CroppedBitmap(image, new Int32Rect(left, top,
                (int)Math.Ceiling(bounds.Right) - left, (int)Math.Ceiling(bounds.Bottom) - top));
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(capture));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
