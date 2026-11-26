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

            ShowForm("record and save", (window, view) =>
            {
                VerifyLayout(window, view, capture: true);
                VerifyForm(view, defaults);
                Fill(view, edited);
                RequireUnchanged(defaults, committed);
                Click(view, "DiscardSettings");
                VerifyForm(view, defaults);
                RequireUnchanged(defaults, committed);

                Fill(view, edited);
                VerifyRecorder(view, edited);
                RequireUnchanged(defaults, committed);
                Click(view, "SaveSettingsAndClose");
            }, save: true);

            VerifySaved(edited);
            committed = File.ReadAllBytes(file);
            ShowForm("reset and cancel", (window, view) =>
            {
                VerifyForm(view, edited);
                VerifyLayout(window, view, capture: false);
                VerifyRecorder(view, edited);
                Click(view, "ResetAllSettings");
                VerifyForm(view, defaults);
                RequireUnchanged(edited, committed);
                SystemCommands.CloseWindow(window);
            }, save: false);
            RequireUnchanged(edited, committed);

            ShowForm("disable and save", (_, view) =>
            {
                VerifyForm(view, edited);
                Click(view, "DisableShortcut");
                Click(view, "SaveSettingsAndClose");
            }, save: true);
            var reloaded = new HostSettings { SettingsFile = file }.Reload();
            Require(reloaded.GlobalHotKey?.Key is null or Key.None && settings.GlobalHotKey?.Key is null or Key.None,
                "Clearing the shortcut must disable it both live and after a restart.");

            ShowForm("restore all defaults", (_, view) =>
            {
                Require(Equals(Find<Button>(view, "GlobalHotKeyEditor").Content, LocalizationManager.Default["Settings.ShortcutDisabled"]),
                    "A disabled shortcut must reopen with an explicit localized status.");
                Click(view, "ResetAllSettings");
                VerifyForm(view, defaults);
                Click(view, "SaveSettingsAndClose");
            }, save: true);
            VerifySaved(defaults);

            // A gesture recorded in an earlier release remains usable on an unrelated save.
            settings.GlobalHotKey = new KeyGestureEx(Key.F6);
            settings.Save();
            ShowForm("preserve earlier shortcut", (_, view) =>
            {
                Require(Equals(Find<Button>(view, "GlobalHotKeyEditor").Content, "F6"), "The stored legacy shortcut must be displayed.");
                Find<CheckBox>(view, "EnableDiagnosticsCheckBox").IsChecked = false;
                Click(view, "SaveSettingsAndClose");
            }, save: true);
            var legacy = new HostSettings { SettingsFile = file }.Reload();
            Require(legacy.GlobalHotKey?.Key == Key.F6 && legacy.GlobalHotKey.Modifiers == ModifierKeys.None && !legacy.EnableDiagnostics,
                "An unrelated save must preserve an older shortcut.");
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

    private static void ShowForm(string scenario, Action<SettingsWindow, SettingsView> interact, bool save)
    {
        Console.WriteLine("Launcher settings opening: " + scenario);
        var window = new SettingsWindow();
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                Console.WriteLine("Launcher settings ready: " + scenario);
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
        Console.WriteLine("Launcher settings closed: " + scenario);
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        Require(result == save, "Unexpected result from the modal settings dialog.");
    }

    private static void VerifyLayout(SettingsWindow window, SettingsView view, bool capture)
    {
        Require(window.Width == 640 && window.Height == 620
                && view.FindName("VersionValue") is null
                && !VisualChildren<ListView>(view).Any() && !VisualChildren<DataGrid>(view).Any(),
            "Expected the dedicated 640×620 settings form with typed fields and no version row.");
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
            var lastField = Find<TextBox>(view, "ILSpyPathTextBox");
            Require(scroll.ComputedVerticalScrollBarVisibility != Visibility.Visible && Inside(lastField, scroll),
                "The default settings size must fit the external tool field above the footer without scrolling.");
            var hint = Find<TextBlock>(view, "ShortcutHint");
            var recorder = Find<Button>(view, "GlobalHotKeyEditor");
            Require(AutomationProperties.GetLabeledBy(recorder) is Label shortcutLabel && ReferenceEquals(shortcutLabel.Target, recorder)
                    && Inside(recorder, scroll) && Inside(hint, scroll) && !string.IsNullOrWhiteSpace(hint.Text)
                    && hint.FontFamily.Equals(recorder.FontFamily) && hint.FontSize == recorder.FontSize
                    && hint.FontFamily.Source.Contains("Microsoft YaHei"),
                "The shortcut recorder and instructions must be labeled and use the form's Latin/CJK typography.");
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
                    if (combo.Name == "MultipleDispatcherModeSelector")
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
        viewer.ScrollToTop();
        Drain();
        foreach (var id in new[] { "GlobalHotKeyEditor", "ResetShortcut", "DisableShortcut" })
        {
            var action = Find<Button>(view, id);
            Require(Inside(action, viewer), "A compact shortcut action was clipped: " + id);
        }

        viewer.ScrollToEnd();
        Drain();
        var path = Find<TextBox>(view, "ILSpyPathTextBox");
        Require(Inside(path, viewer) && viewer.VerticalOffset > 0, "The last settings field must be reachable by scrolling.");
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
        var shortcut = value.GlobalHotKey!;
        Click(view, "GlobalHotKeyEditor");
        SendKey(view, shortcut.Key, shortcut.Modifiers);
    }

    private static void VerifyRecorder(SettingsView view, HostSettings edited)
    {
        var button = Find<Button>(view, "GlobalHotKeyEditor");
        var hint = Find<TextBlock>(view, "ShortcutHint");
        var text = LocalizationManager.Default;
        Console.WriteLine("Recorder: validating individual keys (" + text.LanguageTag + ").");
        Find<Button>(view, "ResetShortcut").Focus();
        button.Focus();
        Require(button.IsKeyboardFocused && Equals(button.Content, text["Settings.ShortcutListening"]),
            "Focusing the shortcut field must start recording.");
        foreach (var number in new[] { Key.D5, Key.D6, Key.NumPad5, Key.NumPad6 })
        {
            SendKey(view, number);
            Require(hint.Text == text["Settings.ShortcutNeedsModifier"], "Bare digits must explain the missing modifier.");
        }

        Drain();
        InspectorFlyoutSmoke.CaptureFramed(view, "settings-shortcut-validation-" + text.LanguageTag);
        Console.WriteLine("Recorder: validating modifiers and reserved shortcuts.");
        SendKey(view, Key.F8, ModifierKeys.Alt);
        Require(hint.Text == text["Settings.ShortcutAlt"], "An Alt shortcut without Ctrl must explain the launcher's modifier requirement.");
        SendKey(view, Key.L, ModifierKeys.Windows);
        Require(hint.Text == text["Settings.ShortcutWindows"], "A Windows shortcut must explain which additional modifiers are supported.");
        SendKey(view, Key.LeftCtrl, ModifierKeys.Control);
        Require(hint.Text == text["Settings.ShortcutNeedsKey"], "A lone modifier must explain the missing second key.");
        foreach (var gesture in new KeyGestureEx[]
                 {
                     new(Key.D5, ModifierKeys.Control),
                     new(Key.B, ModifierKeys.Control),
                     new(Key.C, ModifierKeys.Control),
                     new(Key.V, ModifierKeys.Control),
                     new(Key.Z, ModifierKeys.Control),
                     new(Key.F4, ModifierKeys.Alt),
                     new(Key.S, ModifierKeys.Windows | ModifierKeys.Shift),
                     new(Key.Delete, ModifierKeys.Control | ModifierKeys.Alt)
                 })
        {
            SendKey(view, gesture.Key, gesture.Modifiers);
            Require(hint.Text == text["Settings.ShortcutReserved"], "The recorder accepted a reserved combination: " + gesture);
        }

        Console.WriteLine("Recorder: checking validation, cancellation and reset.");
        Click(view, "SaveSettingsAndClose");
        Require(Window.GetWindow(view).IsVisible && hint.IsVisible && hint.Text == text["Settings.ShortcutReserved"],
            "An unresolved shortcut error must keep the form open.");
        SendKey(view, Key.Escape);
        VerifyForm(view, edited);

        Click(view, "ResetShortcut");
        var defaultKey = new HostSettings().GlobalHotKey!;
        VerifyForm(view, edited, expectedShortcut: defaultKey);

        Console.WriteLine("Recorder: checking focus traversal.");
        Click(view, "GlobalHotKeyEditor");
        SendKey(view, Key.Tab);
        Require(Find<Button>(view, "ResetShortcut").IsKeyboardFocused, "Tab must move from the recorder to the shortcut reset action.");
        button.Focus();
        SendKey(view, Key.Tab, ModifierKeys.Shift);
        Require(Find<ComboBox>(view, "LanguageSelector").IsKeyboardFocusWithin, "Shift+Tab must return to the language selector.");

        Console.WriteLine("Recorder: recording the default and custom combinations.");
        Click(view, "GlobalHotKeyEditor");
        SendKey(view, defaultKey.Key, defaultKey.Modifiers);
        Require(Equals(button.Content, defaultKey.ToString()), "The recorder must accept the factory shortcut including the Windows modifier.");
        Click(view, "GlobalHotKeyEditor");
        var editedShortcut = edited.GlobalHotKey!;
        SendKey(view, editedShortcut.Key, editedShortcut.Modifiers);
        VerifyForm(view, edited);
    }

    // Set only this UI thread's keyboard state and pass the key through WPF's input manager.
    // This exercises preview, bubbling, and focus traversal without sending desktop-wide keystrokes.
    private static void SendKey(SettingsView view, Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        var element = Find<Button>(view, "GlobalHotKeyEditor");
        Require(element.IsKeyboardFocused, "Keyboard input must target the focused recorder.");
        var previous = new byte[256];
        Require(GetKeyboardState(previous), "Unable to read the smoke thread's modifier state.");
        var state = new byte[256];
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            state[0x11] = state[0xA2] = 0x80;
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            state[0x12] = state[0xA4] = 0x80;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            state[0x10] = state[0xA0] = 0x80;
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            state[0x5B] = 0x80;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        state[virtualKey] = 0x80;
        var source = PresentationSource.FromVisual(element);
        try
        {
            Require(SetKeyboardState(state), "Unable to set the smoke thread's modifier state.");
            Deliver(Keyboard.PreviewKeyDownEvent);
            state[virtualKey] = 0;
            if (key == Key.LeftCtrl)
            {
                // The standalone Ctrl test releases both its left-hand and aggregate virtual keys.
                state[0x11] = 0;
            }

            Require(SetKeyboardState(state), "Unable to release the smoke key.");
            Deliver(Keyboard.PreviewKeyUpEvent);
        }
        finally
        {
            var restored = new byte[256];
            Require(SetKeyboardState(previous) && GetKeyboardState(restored) && restored.SequenceEqual(previous),
                "The smoke thread's keyboard state was not restored after input.");
        }

        void Deliver(RoutedEvent routedEvent)
        {
            var input = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = routedEvent,
                Source = Keyboard.FocusedElement
            };
            InputManager.Current.ProcessInput(input);
        }
    }

    private static void VerifyForm(SettingsView view, HostSettings expected, KeyGestureEx? expectedShortcut = null)
    {
        Require(Equals(Find<ComboBox>(view, "LanguageSelector").SelectedValue, expected.LanguageTag)
                && Equals(Find<ComboBox>(view, "MultipleAppDomainModeSelector").SelectedValue, expected.MultipleAppDomainMode)
                && Equals(Find<ComboBox>(view, "MultipleDispatcherModeSelector").SelectedValue, expected.MultipleDispatcherMode)
                && Find<CheckBox>(view, "SetOwnerWindowCheckBox").IsChecked == expected.SetOwnerWindow
                && Find<CheckBox>(view, "ShowActivatedCheckBox").IsChecked == expected.ShowActivated
                && Find<CheckBox>(view, "EnableDiagnosticsCheckBox").IsChecked == expected.EnableDiagnostics
                && Find<CheckBox>(view, "StartAppChooserMinimizedCheckBox").IsChecked == expected.StartAppChooserMinimized
                && Find<TextBox>(view, "ILSpyPathTextBox").Text == expected.ILSpyPath
                && Equals(Find<Button>(view, "GlobalHotKeyEditor").Content, (expectedShortcut ?? expected.GlobalHotKey)?.ToString()),
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

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetKeyboardState([Out] byte[] state);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetKeyboardState(byte[] state);

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
