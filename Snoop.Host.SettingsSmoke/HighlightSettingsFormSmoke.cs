namespace Snoop.Host.SettingsSmoke;

using System;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Snoop.Core;
using Snoop.Infrastructure.SelectionHighlight;
using Snoop.Localization;
using Snoop.Views;

internal static class HighlightSettingsFormSmoke
{
    public static void Verify()
    {
        var options = SelectionHighlightOptions.Default;
        var originalFill = options.Background;
        var originalStroke = options.BorderBrush;
        var originalThickness = options.BorderThickness;
        var originalEnabled = options.HighlightSelectedItem;
        var originalLanguage = InspectorLocalization.Default.LanguageTag;
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            options.Reset();
            options.HighlightSelectedItem = false;
            var defaultStroke = options.BorderBrush;
            Show((window, view) =>
            {
                var fill = Find<TextBox>(view, "FillEditor");
                var stroke = Find<TextBox>(view, "BorderColorEditor");
                var thickness = Find<TextBox>(view, "ThicknessEditor");
                var sample = Find<Rectangle>(view, "HighlightSample");
                var close = Find<Button>(view, "Close");
                var scroll = Find<ScrollViewer>(view, "FormScroll");

                Require(fill.Text.Length == 0 && thickness.Text == "3" && sample.Fill is null
                        && ReferenceEquals(sample.Stroke, defaultStroke) && sample.StrokeThickness == 3,
                    "The form must display the session's actual default brush, empty fill and 3 DIP outline.");
                foreach (var language in new[] { "zh-CN", "en-US" })
                {
                    var localization = InspectorLocalization.Default;
                    localization.SetLanguage(language);
                    foreach (var theme in new[] { ThemeMode.Light, ThemeMode.Dark })
                    {
                        ThemeManager.Current.ApplyTheme(theme, window);
                        Drain();
                        Require(window.Title == localization["HighlightSettings.Title"] && Equals(close.Content, localization["Highlight.Close"]),
                            "The highlight title and actions must follow the active inspector language.");
                        VerifyField(fill, localization["Highlight.Background"]);
                        VerifyField(stroke, localization["Highlight.BorderBrush"]);
                        VerifyField(thickness, localization["Highlight.BorderThickness"]);
                        Require(Inside(fill, scroll) && Inside(stroke, scroll) && Inside(sample, scroll) && Inside(close, view)
                                && scroll.ScrollableHeight < 1 && scroll.ScrollableWidth < 1,
                            "The default dialog must fit the fields, preview and footer.");
                        InspectorFlyoutSmoke.CaptureFramed(view, $"highlight-options-{language}-{theme}");
                    }
                }

                Enter(fill, "#80654321");
                Enter(stroke, "RoyalBlue");
                Enter(thickness, "2.5");
                Drain();
                Require(options.Background is SolidColorBrush { Color: { A: 128, R: 101, G: 67, B: 33 } }
                        && options.BorderBrush is SolidColorBrush blue && blue.Color == Colors.RoyalBlue
                        && options.Pen.Thickness == 2.5 && ReferenceEquals(sample.Fill, options.Background)
                        && ReferenceEquals(sample.Stroke, options.BorderBrush) && sample.StrokeThickness == 2.5,
                    "Enter must apply valid alpha, named colors and fractional thickness to both the sample and the selection pen.");

                var validStroke = options.BorderBrush;
                Enter(stroke, "not-a-color");
                Require(ReferenceEquals(options.BorderBrush, validStroke) && Find<TextBlock>(view, "BorderColorError").IsVisible,
                    "An invalid brush must leave the applied value intact and identify the field.");
                foreach (var invalid in new[] { "-1", "NaN", "Infinity", "invalid" })
                {
                    Enter(thickness, invalid);
                    Require(options.BorderThickness == 2.5 && Find<TextBlock>(view, "ThicknessError").IsVisible,
                        "A non-renderable thickness changed the selection pen: " + invalid);
                }

                Enter(fill, "Transparent");
                Require(options.Background is SolidColorBrush clear && clear.Color == Colors.Transparent,
                    "A transparent brush should remain a brush.");
                Enter(fill, string.Empty);
                Require(options.Background is null, "Clearing the background must restore no fill.");
                Click(view, "ResetSettings");
                Require(options.Background is null && options.BorderThickness == 3 && !options.HighlightSelectedItem
                        && ((SolidColorBrush)options.BorderBrush).Color == ((SolidColorBrush)defaultStroke).Color
                        && !Find<TextBlock>(view, "BorderColorError").IsVisible && !Find<TextBlock>(view, "ThicknessError").IsVisible,
                    "Restore defaults must clear invalid drafts, restore the model defaults and preserve the highlight toggle.");

                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                scroll.ScrollToTop();
                Drain();
                InspectorFlyoutSmoke.CaptureFramed(view, "highlight-options-compact-top-en-US-Dark");
                scroll.ScrollToEnd();
                Drain();
                Require(scroll.ScrollableWidth < 1 && Inside(sample, scroll) && Inside(close, view)
                        && Inside(Find<Button>(view, "ResetSettings"), view),
                    "The narrow form must keep the final preview reachable and both footer actions visible.");
                InspectorFlyoutSmoke.CaptureFramed(view, "highlight-options-compact-bottom-en-US-Dark");
                Click(view, "Close");
            });

            var gradient = new LinearGradientBrush(Colors.Gold, Colors.Blue, 45);
            var precise = new SolidColorBrush(Color.FromScRgb(.31f, .123f, .21f, .54f)) { Opacity = .7 };
            gradient.Freeze();
            precise.Freeze();
            options.Background = gradient;
            options.BorderBrush = precise;
            Show((window, view) =>
            {
                var sample = Find<Rectangle>(view, "HighlightSample");
                Require(ReferenceEquals(sample.Fill, gradient) && ReferenceEquals(sample.Stroke, precise)
                        && Find<TextBox>(view, "FillEditor").Text == nameof(LinearGradientBrush),
                    "Existing WPF brushes must be displayed without conversion.");
                Find<TextBox>(view, "ThicknessEditor").Text = "0";
                SystemCommands.CloseWindow(window);
            });
            Require(options.BorderThickness == 0 && ReferenceEquals(options.Background, gradient) && ReferenceEquals(options.BorderBrush, precise),
                "Caption Close must commit the pending edit and preserve untouched brush instances and scRGB precision.");

            Show((_, view) =>
            {
                var stroke = Find<TextBox>(view, "BorderColorEditor");
                stroke.Text = "#40112233";
                stroke.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, stroke, Find<TextBox>(view, "ThicknessEditor"))
                    { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
                Require(options.BorderBrush is SolidColorBrush edited && edited.Color == Color.FromArgb(64, 17, 34, 51)
                        && edited.Opacity == precise.Opacity,
                    "Moving focus must update the solid color and preserve separate brush opacity.");
                Enter(Find<TextBox>(view, "FillEditor"), "Blue");
                Require(options.Background is SolidColorBrush fill && fill.Color == Colors.Blue,
                    "A deliberate edit must be able to replace an existing gradient with a solid brush.");
                Click(view, "Close");
            });
            Require(CultureInfo.CurrentUICulture.Equals(originalCulture), "The highlight form changed the host UI culture.");
        }
        finally
        {
            options.Background = originalFill;
            options.BorderBrush = originalStroke;
            options.BorderThickness = originalThickness;
            options.HighlightSelectedItem = originalEnabled;
            InspectorLocalization.Default.SetLanguage(originalLanguage);
        }

        Console.WriteLine("PASS: the native highlight form localizes, previews, validates, resets and retains selection brush edits.");
    }

    private static void Show(Action<HighlightSettingsWindow, HighlightSettingsView> interact)
    {
        var window = new HighlightSettingsWindow();
        Exception? failure = null;
        window.ContentRendered += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                const int captionAndSystemMenu = 0x00C80000;
                Require(window.WindowStyle == WindowStyle.SingleBorderWindow && !window.AllowsTransparency
                        && (GetWindowLong(hwnd, -16) & captionAndSystemMenu) == captionAndSystemMenu && (GetWindowLong(hwnd, -20) & 0x80) == 0,
                    "Highlight options requires a standard native caption, Close button and system menu.");
                interact(window, (HighlightSettingsView)window.Content);
                Drain();
                Require(!window.IsVisible, "The selected close action did not dismiss the modal dialog.");
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

        window.ShowDialog();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void VerifyField(TextBox field, string caption)
    {
        Require(AutomationProperties.GetLabeledBy(field) is Label label && ReferenceEquals(label.Target, field)
                && Equals(label.Content, caption) && field.ActualWidth >= 98 && field.ActualHeight >= 30
                && field.FontFamily.Equals(label.FontFamily) && field.FontFamily.Source.Contains("Microsoft YaHei")
                && field.FontSize == label.FontSize && field.FontSize >= 13 && field.IsTabStop,
            "Expected a readable, labeled and keyboard-accessible highlight field: " + caption);
    }

    private static void Enter(TextBox field, string value)
    {
        field.Text = value;
        field.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(field), Environment.TickCount, Key.Enter)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    private static bool Inside(FrameworkElement element, FrameworkElement parent)
    {
        var bounds = element.TransformToAncestor(parent).TransformBounds(new Rect(element.RenderSize));
        return element.IsVisible && bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1
               && bounds.Right <= parent.ActualWidth + 1 && bounds.Bottom <= parent.ActualHeight + 1;
    }

    private static T Find<T>(FrameworkElement view, string name)
        where T : FrameworkElement
        => view.FindName(name) as T ?? throw new InvalidOperationException("Missing highlight control " + name);

    private static void Click(HighlightSettingsView view, string name) =>
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
