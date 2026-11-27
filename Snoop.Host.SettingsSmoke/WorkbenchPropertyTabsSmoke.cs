namespace Snoop.Host.SettingsSmoke;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Core;
using Snoop.Localization;
using Snoop.Views.TriggersTab;
using Snoop.Views.TriggersTab.Triggers;
using Snoop.Windows;

internal static class WorkbenchPropertyTabsSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var text = InspectorLocalization.Default;
        var savedLanguage = text.LanguageTag;
        var savedTheme = Snoop.Core.Settings.Default.ThemeMode;
        var tabs = Find<TabControl>(inspector, "PropertiesWorkbenchPane");
        var dock = Find<TabControl>(inspector, "DiagnosticsWorkbenchPane");
        var bindingsTab = Find<TabItem>(inspector, "WorkbenchBindingsTab");
        var triggersTab = Find<TabItem>(inspector, "WorkbenchTriggersTab");
        var eventsTab = Find<TabItem>(inspector, "WorkbenchRoutedEventsTab");
        var timeline = Find<TabItem>(inspector, "WorkbenchEventsTab");
        var originalTab = tabs.SelectedItem;
        var originalDock = dock.SelectedItem;

        var source = new SaveForm();
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(source.Hint)) { Source = source }));
        var trigger = new Trigger { Property = Button.IsDefaultProperty, Value = true };
        trigger.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 380d));
        style.Triggers.Add(trigger);
        var button = new Button { Name = "SaveButton", Style = style };
        var local = button.SetBinding(ContentControl.ContentProperty, new Binding(nameof(source.Caption)) { Source = source });
        var plain = new Border { Name = "PlainBorder" };
        var panel = new StackPanel();
        panel.Children.Add(button);
        panel.Children.Add(plain);
        var fixture = new Window { Content = panel, Width = 260, Height = 120, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            fixture.Show();
            inspector.ScopeTo(panel);
            SnoopUI.RefreshCommand.Execute(null, inspector);
            Settle();
            var root = inspector.RootTreeItem ?? throw new InvalidOperationException("Missing the controlled target tree.");
            var buttonRow = root.FindNode(button) ?? throw new InvalidOperationException("SaveButton is absent from the target tree.");
            var styled = BindingOperations.GetBindingExpressionBase(button, FrameworkElement.ToolTipProperty);
            Require(styled?.Status == BindingStatus.Active && local.Status == BindingStatus.Active, "The fixture needs active local and style bindings.");

            dock.SelectedItem = timeline;
            text.SetLanguage("en-US");
            ThemeManager.Current.ApplyTheme(ThemeMode.Light, inspector);
            inspector.CurrentSelection = buttonRow;
            var bindingView = Open(bindingsTab);
            var bindings = Find<ListView>(bindingView, "BindingsList");
            var noBindings = Find<TextBlock>(bindingView, "BindingsEmpty");
            VerifyVisibleRow(bindings, "PropertyName", "Content", nameof(source.Caption), "Active");
            VerifyVisibleRow(bindings, "PropertyName", "ToolTip", nameof(source.Hint), "Active");
            Require(!noBindings.IsVisible, "A populated binding list must hide its empty state.");
            LauncherAppearanceSmoke.CaptureIfRequested(tabs, "property-tabs-bindings-Light-en-US");

            text.SetLanguage("zh-CN");
            Settle();
            VerifyVisibleRow(bindings, "PropertyName", "Content", "路径：" + nameof(source.Caption), "状态：Active");
            text.SetLanguage("en-US");
            local = button.SetBinding(ContentControl.ContentProperty, new Binding(nameof(source.PendingText)) { Source = source });
            SnoopUI.RefreshCommand.Execute(null, inspector);
            Settle();
            var refreshed = VerifyVisibleRow(bindings, "PropertyName", "Content", nameof(source.PendingText), "Active");
            Require(!refreshed.Contains(nameof(source.Caption)) && bindingsTab.IsSelected && ReferenceEquals(inspector.Target, button),
                "F5 must replace the old binding details without changing the target or the selected tab.");
            inspector.Target = plain;
            Settle();
            Require(bindings.Items.Count == 0, "Selecting PlainBorder must clear both old bindings.");
            VerifyEmpty(noBindings, "SelectionDetails.NoBindings");
            text.SetLanguage("zh-CN");
            Settle();
            VerifyEmpty(noBindings, "SelectionDetails.NoBindings");
            ThemeManager.Current.ApplyTheme(ThemeMode.Dark, inspector);

            var eventView = Open(eventsTab);
            var events = Find<ListView>(eventView, "RoutedEventsList");
            var noEvents = Find<TextBlock>(eventView, "RoutedEventsEmpty");
            Require(!Rows(events, "EventName", "Click").Any(), "PlainBorder must not inherit ButtonBase.Click.");
            inspector.Target = button;
            Settle();
            VerifyVisibleRow(events, "EventName", "Loaded", "FrameworkElement", "Direct");
            VerifyVisibleRow(events, "EventName", "Click", "ButtonBase", "Bubble");
            Require(!noEvents.IsVisible, "A populated event list must hide its empty state.");
            LauncherAppearanceSmoke.CaptureIfRequested(tabs, "property-tabs-events-Dark-zh-CN");

            // Select an actual non-routed object through a scoped tree root.
            var swatch = new SolidColorBrush(Colors.SteelBlue);
            inspector.CurrentSelection = null;
            inspector.ScopeTo(swatch);
            SnoopUI.RefreshCommand.Execute(null, inspector);
            inspector.CurrentSelection = inspector.RootTreeItem;
            Settle();
            Require(ReferenceEquals(inspector.Target, swatch) && events.Items.Count == 0, "Selecting a brush must clear the button's routed events.");
            VerifyEmpty(noEvents, "SelectionDetails.NoRoutedEvents");
            text.SetLanguage("en-US");
            Settle();
            VerifyEmpty(noEvents, "SelectionDetails.NoRoutedEvents");

            var triggers = (TriggersView)Open(triggersTab);
            VerifyTriggerEmpty(triggers);
            inspector.CurrentSelection = null;
            inspector.ScopeTo(panel);
            SnoopUI.RefreshCommand.Execute(null, inspector);
            inspector.Target = button;
            Settle();
            Require(InspectorTableSmoke.Descendants(triggers).OfType<HeaderedContentControl>()
                        .Any(row => row.IsVisible && row.DataContext is TriggerItemBase { TriggerSource: TriggerSource.Style }),
                "The selected button's style trigger must appear in the visible trigger list.");
            inspector.Target = plain;
            Settle();
            VerifyTriggerEmpty(triggers);
            Require(ReferenceEquals(inspector.Target, plain) && ReferenceEquals(dock.SelectedItem, timeline)
                    && ReferenceEquals(local, BindingOperations.GetBindingExpressionBase(button, ContentControl.ContentProperty))
                    && ReferenceEquals(styled, BindingOperations.GetBindingExpressionBase(button, FrameworkElement.ToolTipProperty)),
                "Switching detail tabs must preserve the timeline and the fixture's bindings.");
        }
        finally
        {
            tabs.SelectedItem = originalTab;
            dock.SelectedItem = originalDock;
            inspector.CurrentSelection = null;
            inspector.Unscope();
            text.SetLanguage(savedLanguage);
            ThemeManager.Current.ApplyTheme(savedTheme, inspector);
            fixture.Close();
        }

        Console.WriteLine("PASS: selection detail tabs show real local and style bindings, triggers and inherited routed events, and clear stale rows in both languages.");

        FrameworkElement Open(TabItem tab)
        {
            var previousTarget = inspector.Target;
            tabs.SelectedItem = tab;
            Settle();
            var content = (FrameworkElement)tab.Content;
            Require(tab.IsSelected && content.IsVisible && ReferenceEquals(previousTarget, inspector.Target), "Opening a detail tab must display it and retain the selected target.");
            return content;
        }
    }

    private static void VerifyEmpty(TextBlock empty, string key)
    {
        var expected = InspectorLocalization.Default[key];
        Require(empty.IsVisible && !string.IsNullOrWhiteSpace(expected) && !expected.StartsWith("[", StringComparison.Ordinal) && empty.Text == expected,
            "Missing localized empty state for " + key + ".");
    }

    private static void VerifyTriggerEmpty(TriggersView view)
    {
        var message = InspectorLocalization.Default["Triggers.NoTriggers"];
        Require(view.TriggerItems?.IsEmpty == true && InspectorTableSmoke.Descendants(view).OfType<TextBlock>().Any(block => block.IsVisible && block.Text == message),
            "A target with no triggers must show the native localized empty state.");
    }

    private static IEnumerable<object> Rows(ListView list, string property, string name)
        => list.Items.Cast<object>().Where(item => Equals(TypeDescriptor.GetProperties(item)[property]?.GetValue(item), name));

    private static string VerifyVisibleRow(ListView list, string property, string name, params string[] terms)
    {
        var item = Rows(list, property, name).Single();
        list.ScrollIntoView(item);
        list.UpdateLayout();
        var row = list.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem
            ?? throw new InvalidOperationException("Could not realize " + name + ".");
        var displayed = string.Join(" ", InspectorTableSmoke.Descendants(row).OfType<TextBlock>().Where(cell => cell.IsVisible).Select(cell => cell.Text));
        Require(row.IsVisible && displayed.Contains(name) && terms.All(displayed.Contains), $"The {name} row does not display the expected metadata: {displayed}");
        return displayed;
    }

    private static T Find<T>(FrameworkElement root, string name)
        where T : FrameworkElement
        => root.FindName(name) as T ?? throw new InvalidOperationException("Missing selection details control " + name + ".");

    private static void Settle() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SaveForm
    {
        public string Caption => "Save";

        public string Hint => "Save the changes";

        public string PendingText => "Save pending changes";
    }
}
