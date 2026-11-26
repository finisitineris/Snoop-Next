namespace Snoop.Host.SettingsSmoke;

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Core;
using Snoop.Infrastructure;
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
            var diagnosticLevel = (TextBlock)((GridView)list.View).Columns[1].Header;
            using var events = new EventsView();
            var eventChooser = (NoSelectionComboBox)events.FindName("EventChooser");

            RequireText(windows[0].Title, "编辑筛选集", "Filter editor title");
            RequireText(windows[1].Title, "保存截图", "Screenshot title");
            RequireText(windows[2].Title, "缩放查看器", "Zoomer title");
            RequireText(diagnosticLevel.Text, "级别", "Diagnostics level heading");
            RequireText(eventChooser.Header, "更改跟踪的事件", "Event chooser heading");

            var editor = (EditUserFilters)windows[0];
            var readOnly = new PropertyFilterSet { DisplayName = localization["PropertyInspector.FilterLayout"], Properties = new[] { "Width", "Height" }, IsReadOnly = true };
            var editable = new PropertyFilterSet { DisplayName = "Button", Properties = new[] { "Content", "Command", "IsEnabled" } };
            editor.UserFilters = new[] { readOnly, editable };
            editor.Show();
            var filters = (ListBox)editor.FindName("filterSetList");
            filters.SelectedItem = editable;
            VerifyFilterForm(editor, ThemeMode.Light);

            localization.SetLanguage("en-US");
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

            RequireText(windows[0].Title, "Edit Filter Sets", "Filter editor title");
            RequireText(windows[1].Title, "Save Screenshot", "Screenshot title");
            RequireText(windows[2].Title, "Zoomer", "Zoomer title");
            RequireText(diagnosticLevel.Text, "Level", "Diagnostics level heading");
            RequireText(eventChooser.Header, "Change tracked events", "Event chooser heading");
            editor.Width = editor.MinWidth;
            editor.Height = editor.MinHeight;
            VerifyFilterForm(editor, ThemeMode.Dark);
            var name = (TextBox)editor.FindName("NameEditor");
            var properties = (TextBox)editor.FindName("PropertiesEditor");
            if (name.IsReadOnly || properties.IsReadOnly || name.Text != editable.DisplayName || properties.Text != "Content,Command,IsEnabled")
            {
                throw new InvalidOperationException("The selected user filter must expose its editable name and property list.");
            }

            Click("AddItem");
            var added = filters.SelectedItem;
            Click("MoveUp");
            if (filters.SelectedIndex != 1 || !ReferenceEquals(filters.SelectedItem, added))
            {
                throw new InvalidOperationException("Moving a user filter must preserve its selection.");
            }

            Click("MoveDown");
            Click("DeleteItem");
            filters.SelectedItem = readOnly;
            editor.UpdateLayout();
            if (filters.Items.Count != 2 || !name.IsReadOnly || !properties.IsReadOnly || ((Button)editor.FindName("DeleteItem")).IsEnabled)
            {
                throw new InvalidOperationException("Existing built-in filters must remain read-only and protected from deletion.");
            }

            Console.WriteLine("PASS: the named filter-set form renders and preserves selection actions and read-only filters; auxiliary views live-switch language.");

            void Click(string id) => ((Button)editor.FindName(id)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

    private static void VerifyFilterForm(EditUserFilters editor, ThemeMode theme)
    {
        ThemeManager.Current.ApplyTheme(theme, editor);
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        var surface = (Grid)editor.FindName("FormSurface");
        var localization = InspectorLocalization.Default;
        editor.UpdateLayout();
        InspectorFlyoutSmoke.CaptureFramed(surface, $"user-filter-settings-{localization.LanguageTag}-{theme}");
        foreach (var id in new[] { "NameEditor", "PropertiesEditor", "AddItem", "DeleteItem", "MoveUp", "MoveDown", "Accept", "Cancel" })
        {
            var field = (Control)editor.FindName(id);
            var bounds = field.TransformToAncestor(surface).TransformBounds(new Rect(field.RenderSize));
            if (!field.IsVisible || field.ActualHeight < 30 || field.ActualWidth < 34 || !new Rect(surface.RenderSize).Contains(bounds)
                || field.FontSize < 13 || !field.FontFamily.Source.Contains("Microsoft YaHei"))
            {
                throw new InvalidOperationException($"Filter field {id}: visibility {field.IsVisible}, size {field.RenderSize}, bounds {bounds} inside {surface.RenderSize}, font {field.FontSize} {field.FontFamily.Source}.");
            }

            if (field is TextBox && (field.ActualWidth < 200 || AutomationProperties.GetLabeledBy(field) is not Label label
                                    || !ReferenceEquals(label.Target, field)))
            {
                throw new InvalidOperationException("Expected a flexible field with a visible associated label: " + id);
            }
        }

        RequireText(AutomationProperties.GetName((Button)editor.FindName("MoveUp")), localization["FilterEditor.MoveUp"], "Move-up accessible name");
        RequireText(AutomationProperties.GetName((Button)editor.FindName("MoveDown")), localization["FilterEditor.MoveDown"], "Move-down accessible name");
    }

    private static void RequireText(string? actual, string expected, string control)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{control} expected '{expected}', got '{actual}'");
        }
    }
}
