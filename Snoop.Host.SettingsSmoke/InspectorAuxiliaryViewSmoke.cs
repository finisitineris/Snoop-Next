namespace Snoop.Host.SettingsSmoke;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Localization;
using Snoop.Views;
using Snoop.Windows;

internal static class InspectorAuxiliaryViewSmoke
{
    public static void Verify()
    {
        var localization = InspectorLocalization.Default;
        var originalLanguage = localization.LanguageTag;
        localization.SetLanguage("zh-CN");
        var windows = new Window[] { new EditUserFilters(), new ScreenshotDialog(), new Zoomer() };
        try
        {
            var diagnostics = new DiagnosticsView();
            var list = (ListView)diagnostics.FindName("diagnostics");
            var diagnosticLevel = (TextBlock)((GridView)list.View).Columns[0].Header;
            using var events = new EventsView();
            var eventChooser = (NoSelectionComboBox)events.FindName("EventChooser");

            RequireText(windows[0].Title, "编辑筛选集", "Filter editor title");
            RequireText(windows[1].Title, "保存截图", "Screenshot title");
            RequireText(windows[2].Title, "缩放查看器", "Zoomer title");
            RequireText(diagnosticLevel.Text, "级别", "Diagnostics level heading");
            RequireText(eventChooser.Header, "更改跟踪的事件", "Event chooser heading");

            localization.SetLanguage("en-US");
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

            RequireText(windows[0].Title, "Edit Filter Sets", "Filter editor title");
            RequireText(windows[1].Title, "Save Screenshot", "Screenshot title");
            RequireText(windows[2].Title, "Zoomer", "Zoomer title");
            RequireText(diagnosticLevel.Text, "Level", "Diagnostics level heading");
            RequireText(eventChooser.Header, "Change tracked events", "Event chooser heading");
            Console.WriteLine("PASS: inspector dialogs and panels load Chinese and live-switch to English");
        }
        finally
        {
            foreach (var window in windows)
            {
                window.Close();
            }

            localization.SetLanguage(originalLanguage);
        }
    }

    private static void RequireText(string? actual, string expected, string control)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{control} expected '{expected}', got '{actual}'");
        }
    }
}
