namespace Snoop.Host.SettingsSmoke;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Core;
using Snoop.Data;
using Snoop.Next.Localization;
using Snoop.Views;
using HostSettings = Snoop.Settings;

internal static class LauncherSettingsFormSmoke
{
    public static void Verify(string file)
    {
        var settings = HostSettings.Default;
        var originalFile = File.ReadAllBytes(file);
        var originalLanguage = LocalizationManager.Default.LanguageTag;
        var originalCulture = CultureInfo.CurrentUICulture;
        var defaults = new HostSettings();
        var edited = new HostSettings
        {
            LanguageTag = "en-US",
            MultipleAppDomainMode = MultipleAppDomainMode.AlwaysUse,
            MultipleDispatcherMode = MultipleDispatcherMode.NeverUse,
            SetOwnerWindow = false,
            ShowActivated = false,
            EnableDiagnostics = false,
            StartAppChooserMinimized = true,
            ILSpyPath = @"C:\Tools with spaces\ILSpy.exe",
            GlobalHotKey = new KeyGestureEx(Key.F8, ModifierKeys.Control | ModifierKeys.Shift)
        };

        try
        {
            settings.Reset();
            settings.Save();
            LocalizationManager.Default.SetLanguage(settings.LanguageTag);
            var committed = File.ReadAllBytes(file);

            ShowForm((window, view) =>
            {
                VerifyLayout(window, view, capture: true);
                VerifyForm(view, defaults);
                Fill(view, edited);
                RequireUnchanged(defaults, committed);
                Click(view, "DiscardSettings");
                VerifyForm(view, defaults);
                RequireUnchanged(defaults, committed);

                Fill(view, edited);
                Find<TextBox>(view, "GlobalHotKeyEditor").Text = "Ctrl+NoSuchKey";
                Click(view, "SaveSettingsAndClose");
                Require(window.IsVisible && Find<TextBlock>(view, "GlobalHotKeyError").IsVisible,
                    "An invalid shortcut must keep the form open and show a validation message.");
                RequireUnchanged(defaults, committed);
                Find<TextBox>(view, "GlobalHotKeyEditor").Text = "Ctrl+Shift+F8";
                Click(view, "SaveSettingsAndClose");
            }, save: true);

            VerifySaved(edited);
            committed = File.ReadAllBytes(file);
            ShowForm((window, view) =>
            {
                VerifyForm(view, edited);
                VerifyLayout(window, view, capture: false);
                Click(view, "ResetAllSettings");
                VerifyForm(view, defaults);
                RequireUnchanged(edited, committed);
                SystemCommands.CloseWindow(window);
            }, save: false);
            RequireUnchanged(edited, committed);

            ShowForm((_, view) =>
            {
                VerifyForm(view, edited);
                Find<TextBox>(view, "GlobalHotKeyEditor").Clear();
                Click(view, "SaveSettingsAndClose");
            }, save: true);
            var reloaded = new HostSettings { SettingsFile = file }.Reload();
            Require(reloaded.GlobalHotKey?.Key is null or Key.None && settings.GlobalHotKey?.Key is null or Key.None,
                "Clearing the shortcut must disable it both live and after a restart.");

            ShowForm((_, view) =>
            {
                Require(Find<TextBox>(view, "GlobalHotKeyEditor").Text.Length == 0, "A disabled shortcut must reopen blank.");
                Click(view, "ResetAllSettings");
                VerifyForm(view, defaults);
                Click(view, "SaveSettingsAndClose");
            }, save: true);
            VerifySaved(defaults);
            Require(CultureInfo.CurrentUICulture.Equals(originalCulture), "The settings window changed the dispatcher UI culture.");
        }
        finally
        {
            File.WriteAllBytes(file, originalFile);
            settings.Reload();
            LocalizationManager.Default.SetLanguage(originalLanguage);
        }

        Console.WriteLine("PASS: the native launcher settings form renders localized, readable controls and commits, discards, resets and validates preferences.");

        void VerifySaved(HostSettings expected)
        {
            VerifySettings(settings, expected);
            VerifySettings(new HostSettings { SettingsFile = file }.Reload(), expected);
            Require(LocalizationManager.Default.LanguageTag == expected.LanguageTag, "Save must apply the selected language.");
        }

        void RequireUnchanged(HostSettings expected, byte[] existingFile)
        {
            VerifySettings(settings, expected);
            Require(File.ReadAllBytes(file).SequenceEqual(existingFile) && LocalizationManager.Default.LanguageTag == expected.LanguageTag,
                "A pending edit changed the settings file or the launcher's active language.");
        }
    }

    private static void ShowForm(Action<SettingsWindow, SettingsView> interact, bool save)
    {
        var window = new SettingsWindow();
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                const int captionAndSystemMenu = 0x00C80000;
                const int toolWindow = 0x00000080;
                Require(window.IsVisible && window.WindowStyle == WindowStyle.SingleBorderWindow && !window.AllowsTransparency
                        && hwnd != IntPtr.Zero && (GetWindowLong(hwnd, -16) & captionAndSystemMenu) == captionAndSystemMenu
                        && (GetWindowLong(hwnd, -20) & toolWindow) == 0 && window.Title == LocalizationManager.Default["Chooser.Settings"],
                    "Settings must open as a standard titled Windows dialog.");
                interact(window, (SettingsView)window.Content);
                // The system Close command posts a native message. Let it run before checking the result.
                Drain();
                Require(!window.IsVisible, "The requested action did not close the settings dialog.");
            }
            catch (Exception ex)
            {
                failure = ex;
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        }));

        var result = window.ShowDialog();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        Require(result == save, "Unexpected result from the modal settings dialog.");
    }

    private static void VerifyLayout(SettingsWindow window, SettingsView view, bool capture)
    {
        Require(window.Width == 640 && window.Height == 620
                && Find<TextBlock>(view, "VersionValue").Text == HostSettings.Default.Version
                && !VisualChildren<ListView>(view).Any() && !VisualChildren<DataGrid>(view).Any(),
            "Expected the dedicated 640×620 settings form with a read-only version and typed fields.");
        var labels = new[] { "LanguageSelector", "MultipleAppDomainModeSelector", "MultipleDispatcherModeSelector" };
        var keys = new[] { "Settings.Language", "Settings.MultipleAppDomains", "Settings.MultipleDispatchers" };
        var languageOptions = new[] { "Settings.Chinese", "Settings.English", "Settings.FollowSystem" };
        var modeOptions = new[] { "Settings.ModeAsk", "Settings.ModeAlways", "Settings.ModeNever" };
        for (var index = 0; index < labels.Length; index++)
        {
            var combo = Find<ComboBox>(view, labels[index]);
            Require(AutomationProperties.GetLabeledBy(combo) is Label label && ReferenceEquals(label.Target, combo)
                    && Equals(label.Content, LocalizationManager.Default[keys[index]]),
                "The settings selector must have its visible localized label: " + combo.Name);
        }

        foreach (var theme in new[] { ThemeMode.Light, ThemeMode.Dark })
        {
            ThemeManager.Current.ApplyTheme(theme, window);
            var scroll = Find<ScrollViewer>(view, "SettingsScrollViewer");
            scroll.ScrollToTop();
            Drain();
            if (capture)
            {
                InspectorFlyoutSmoke.CaptureFramed(view, "settings-" + theme);
            }

            for (var index = 0; index < labels.Length; ++index)
            {
                var combo = Find<ComboBox>(view, labels[index]);
                combo.BringIntoView();
                Drain();
                combo.IsDropDownOpen = true;
                try
                {
                    var popup = InspectorFlyoutSmoke.VerifyPopup(combo, window);
                    VerifyChoices(combo, popup, index == 0 ? languageOptions : modeOptions);
                    if (index == 1)
                    {
                        InspectorFlyoutSmoke.CaptureFramed(view, $"settings-dropdown-{LocalizationManager.Default.LanguageTag}-{theme}", popup);
                    }
                }
                finally
                {
                    combo.IsDropDownOpen = false;
                    Drain();
                }
            }
        }

        window.Width = 480;
        window.Height = 360;
        Drain();
        var viewer = Find<ScrollViewer>(view, "SettingsScrollViewer");
        Require(viewer.ScrollableHeight > 0 && viewer.ScrollableWidth < 1, "The compact settings form must scroll vertically.");
        viewer.ScrollToEnd();
        Drain();
        var version = Find<TextBlock>(view, "VersionValue");
        Require(Inside(version, viewer) && viewer.VerticalOffset > 0, "The last settings field must be reachable by scrolling.");
        foreach (var id in new[] { "ResetAllSettings", "DiscardSettings", "SaveSettingsAndClose" })
        {
            Require(Inside(Find<Button>(view, id), view), "A compact settings action was clipped: " + id);
        }

        window.Width = 640;
        window.Height = 620;
        viewer.ScrollToTop();
        Drain();
    }

    private static void VerifyChoices(ComboBox combo, FrameworkElement popup, string[] keys)
    {
        var items = combo.Items.Cast<ComboBoxItem>().ToArray();
        var selection = (ContentPresenter)combo.Template.FindName("Selection", combo);
        var selectedText = VisualChildren<TextBlock>(selection).Single();
        Require(items.Length == keys.Length && selectedText.Text == (string)((ComboBoxItem)combo.SelectedItem).Content
                && Inside(selectedText, combo) && popup.ActualWidth + 1 >= combo.ActualWidth,
            "The closed selector must show its selected text and the dropdown must span the field: " + combo.Name);
        for (var i = 0; i < items.Length; i++)
        {
            var row = items[i];
            var caption = VisualChildren<TextBlock>(row).Single();
            var expected = LocalizationManager.Default[keys[i]];
            var natural = new TextBlock { Text = expected, FontFamily = caption.FontFamily, FontSize = caption.FontSize };
            natural.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Require(caption.Text == expected && caption.FontSize == selectedText.FontSize && caption.FontSize >= 13
                    && caption.FontFamily.Equals(selectedText.FontFamily) && caption.FontFamily.Source.Contains("Microsoft YaHei")
                    && caption.ActualWidth + 1 >= natural.DesiredSize.Width && Inside(caption, row) && Inside(row, popup),
                $"Dropdown option '{expected}' in {combo.Name} must be visible, uncut and use the selected field's Latin/CJK font.");
        }
    }

    private static void Fill(SettingsView view, HostSettings value)
    {
        Find<ComboBox>(view, "LanguageSelector").SelectedValue = value.LanguageTag;
        Find<ComboBox>(view, "MultipleAppDomainModeSelector").SelectedValue = value.MultipleAppDomainMode;
        Find<ComboBox>(view, "MultipleDispatcherModeSelector").SelectedValue = value.MultipleDispatcherMode;
        Find<CheckBox>(view, "SetOwnerWindowCheckBox").IsChecked = value.SetOwnerWindow;
        Find<CheckBox>(view, "ShowActivatedCheckBox").IsChecked = value.ShowActivated;
        Find<CheckBox>(view, "EnableDiagnosticsCheckBox").IsChecked = value.EnableDiagnostics;
        Find<CheckBox>(view, "StartAppChooserMinimizedCheckBox").IsChecked = value.StartAppChooserMinimized;
        Find<TextBox>(view, "ILSpyPathTextBox").Text = value.ILSpyPath ?? string.Empty;
        Find<TextBox>(view, "GlobalHotKeyEditor").Text = value.GlobalHotKey?.ToString() ?? string.Empty;
    }

    private static void VerifyForm(SettingsView view, HostSettings expected)
    {
        Require(Equals(Find<ComboBox>(view, "LanguageSelector").SelectedValue, expected.LanguageTag)
                && Equals(Find<ComboBox>(view, "MultipleAppDomainModeSelector").SelectedValue, expected.MultipleAppDomainMode)
                && Equals(Find<ComboBox>(view, "MultipleDispatcherModeSelector").SelectedValue, expected.MultipleDispatcherMode)
                && Find<CheckBox>(view, "SetOwnerWindowCheckBox").IsChecked == expected.SetOwnerWindow
                && Find<CheckBox>(view, "ShowActivatedCheckBox").IsChecked == expected.ShowActivated
                && Find<CheckBox>(view, "EnableDiagnosticsCheckBox").IsChecked == expected.EnableDiagnostics
                && Find<CheckBox>(view, "StartAppChooserMinimizedCheckBox").IsChecked == expected.StartAppChooserMinimized
                && Find<TextBox>(view, "ILSpyPathTextBox").Text == expected.ILSpyPath
                && Find<TextBox>(view, "GlobalHotKeyEditor").Text == expected.GlobalHotKey?.ToString(),
            "The displayed fields did not match the expected pending preferences.");
    }

    private static void VerifySettings(HostSettings value, HostSettings expected)
    {
        Require(value.LanguageTag == expected.LanguageTag && value.MultipleAppDomainMode == expected.MultipleAppDomainMode
                && value.MultipleDispatcherMode == expected.MultipleDispatcherMode && value.SetOwnerWindow == expected.SetOwnerWindow
                && value.ShowActivated == expected.ShowActivated && value.EnableDiagnostics == expected.EnableDiagnostics
                && value.StartAppChooserMinimized == expected.StartAppChooserMinimized && value.ILSpyPath == expected.ILSpyPath
                && Equals(value.GlobalHotKey, expected.GlobalHotKey), "Unexpected committed launcher preferences.");
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent)
        where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T element)
            {
                yield return element;
            }

            foreach (var descendant in VisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool Inside(FrameworkElement element, FrameworkElement parent)
    {
        var bounds = element.TransformToAncestor(parent).TransformBounds(new Rect(element.RenderSize));
        return element.IsVisible && bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1
               && bounds.Right <= parent.ActualWidth + 1 && bounds.Bottom <= parent.ActualHeight + 1;
    }

    private static T Find<T>(SettingsView view, string name)
        where T : FrameworkElement
        => view.FindName(name) as T ?? throw new InvalidOperationException("Missing settings field " + name);

    private static void Click(SettingsView view, string name) =>
        Find<Button>(view, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
