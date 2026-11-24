namespace Snoop.Host.SettingsSmoke;

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Snoop.AttachedProperties;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Views;
using Snoop.Windows;

internal static class WorkbenchEventsDockSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        if (inspector.FindName("DiagnosticsWorkbenchPane") is not TabControl diagnostics
            || inspector.FindName("WorkbenchEventsTab") is not TabItem events
            || !diagnostics.Items.Contains(events))
        {
            throw new InvalidOperationException("The Events tab must be in the diagnostics dock.");
        }

        var chrome = Find<DockPanel>(inspector, "WorkbenchChrome");
        var properties = Find<TabControl>(inspector, "PropertiesWorkbenchPane");
        var previewTab = Find<TabItem>(inspector, "WorkbenchPreviewTab");
        var diagnosticsTab = Find<TabItem>(inspector, "WorkbenchDiagnosticsTab");
        var showDiagnostics = Find<MenuItem>(inspector, "ShowDiagnosticsPaneItem");
        var eventsView = Find<EventsView>(inspector, "eventsView");
        var filter = Find<TextBox>(eventsView, "EventFilter");
        var label = Find<Label>(eventsView, "EventFilterLabel");
        var maxEvents = Find<TextBox>(eventsView, "MaxEventsEditor");
        var eventTree = Find<TreeView>(eventsView, "EventTree");
        Require(!properties.Items.Contains(events) && ReferenceEquals(events.Content, eventsView)
                && ReferenceEquals(diagnostics.Items[0], diagnosticsTab) && ReferenceEquals(diagnostics.Items[1], previewTab),
            "The dock must contain the existing Events view alongside the same Diagnostics and Preview tabs.");
        Require(ReferenceEquals(label.Target, filter), "The event filter label must identify its text box.");
        Require(ReferenceEquals(eventTree.ItemsSource, eventsView.DisplayedEvents),
            "The event tree must show the filtered view of its capture history.");

        var savedLanguage = InspectorLocalization.Default.LanguageTag;
        var savedTab = diagnostics.SelectedItem;
        var savedVisibility = showDiagnostics.IsChecked;
        var savedQuery = filter.Text;
        var savedLocalSnoopPart = eventsView.ReadLocalValue(SnoopAttachedProperties.IsSnoopPartProperty);
        // The hidden fixture has no source window around the view's logical tree.
        SnoopAttachedProperties.SetIsSnoopPart(eventsView, true);
        var target = new Border();
        try
        {
            diagnostics.SelectedItem = events;
            InspectorLocalization.Default.SetLanguage("en-US");
            SetQuery(string.Empty);
            Require(label.Content as string == "Filter events", "Missing English event filter label.");
            VerifyMinimumLayout();
            ClearEvents();

            RaiseMouseEvent(Mouse.MouseDownEvent);
            RaiseMouseEvent(Mouse.MouseUpEvent);
            VerifyEvents(Mouse.MouseDownEvent, Mouse.MouseUpEvent);
            Require(eventsView.InterestingEvents.Count == 2
                    && eventsView.InterestingEvents.All(item => ReferenceEquals(item.Originator.Handler, target)),
                "The Events dock must show actual routed events from the fixture target.");

            SetQuery("  mOuSeDo  ");
            VerifyEvents(Mouse.MouseDownEvent);
            RaiseMouseEvent(Mouse.MouseDownEvent);
            RaiseMouseEvent(Mouse.MouseUpEvent);
            VerifyEvents(Mouse.MouseDownEvent, Mouse.MouseDownEvent);
            Require(eventsView.InterestingEvents.Count == 4, "Filtering must leave the full capture history intact.");

            InspectorLocalization.Default.SetLanguage("zh-CN");
            diagnostics.SelectedItem = previewTab;
            SetDiagnosticsVisible(false);
            SetDiagnosticsVisible(true);
            Require(ReferenceEquals(diagnostics.SelectedItem, previewTab),
                "Collapsing and restoring the diagnostics dock must preserve its active tab.");
            diagnostics.SelectedItem = events;
            DrainDispatcher();
            Require(label.Content as string == "筛选事件" && filter.Text == "  mOuSeDo  ",
                "The event filter must retain its query across tab changes and language changes.");
            VerifyMinimumLayout();
            VerifyEvents(Mouse.MouseDownEvent, Mouse.MouseDownEvent);

            SetQuery("no fixture event matches this");
            VerifyEvents();
            SetQuery(string.Empty);
            VerifyEvents(Mouse.MouseDownEvent, Mouse.MouseUpEvent, Mouse.MouseDownEvent, Mouse.MouseUpEvent);

            SetQuery("up");
            VerifyEvents(Mouse.MouseUpEvent, Mouse.MouseUpEvent);
            ClearEvents();
            SetQuery(string.Empty);
            VerifyEvents();
            Require(eventsView.InterestingEvents.Count == 0, "Clear must empty matching and hidden event history.");
        }
        finally
        {
            ClearEvents();
            SetQuery(savedQuery);
            diagnostics.SelectedItem = savedTab;
            SetDiagnosticsVisible(savedVisibility);
            if (savedLocalSnoopPart == DependencyProperty.UnsetValue)
            {
                eventsView.ClearValue(SnoopAttachedProperties.IsSnoopPartProperty);
            }
            else
            {
                eventsView.SetValue(SnoopAttachedProperties.IsSnoopPartProperty, savedLocalSnoopPart);
            }

            InspectorLocalization.Default.SetLanguage(savedLanguage);
            DrainDispatcher();
        }

        Console.WriteLine("PASS: diagnostics dock preserves and filters real routed events across tab changes");

        void SetQuery(string query)
        {
            filter.Text = query;
            DrainDispatcher();
        }

        void VerifyMinimumLayout()
        {
            var frame = SystemParameters.WindowNonClientFrameThickness;
            var bounds = new Rect(0, 0, inspector.MinWidth - frame.Left - frame.Right,
                inspector.MinHeight - frame.Top - frame.Bottom);
            chrome.Measure(bounds.Size);
            chrome.Arrange(bounds);
            DrainDispatcher();

            Require(filter.ActualWidth > 16 && filter.ActualHeight > 0
                    && maxEvents.ActualWidth > 0 && maxEvents.ActualHeight > 0
                    && maxEvents.TranslatePoint(new Point(maxEvents.ActualWidth, 0), eventsView).X <= eventsView.ActualWidth + 1
                    && eventTree.ActualHeight > 0
                    && eventTree.TranslatePoint(new Point(0, eventTree.ActualHeight), chrome).Y <= bounds.Height + 1,
                "The Events dock must leave room for the search field and event list at minimum window size.");
            if (eventTree.HasItems)
            {
                var firstRow = eventTree.ItemContainerGenerator.ContainerFromIndex(0) as TreeViewItem;
                Require(firstRow is not null && firstRow.ActualHeight > 0 && firstRow.ActualHeight <= eventTree.ActualHeight,
                    $"The Events dock must fit a captured event at minimum height. Row {firstRow?.ActualHeight}, list {eventTree.ActualHeight}.");
            }
        }

        void ClearEvents()
        {
            EventsView.ClearCommand.Execute(null, eventsView);
            DrainDispatcher();
        }

        void RaiseMouseEvent(RoutedEvent routedEvent)
        {
            var input = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = routedEvent,
            };
            target.RaiseEvent(input);
            DrainDispatcher();
        }

        void VerifyEvents(params RoutedEvent[] expected)
        {
            var actual = eventTree.Items.Cast<TrackedEvent>().Select(item => item.EventArgs.RoutedEvent);
            Require(actual.SequenceEqual(expected),
                "Unexpected docked events for query '" + filter.Text + "'.");
        }

        void SetDiagnosticsVisible(bool visible)
        {
            if (showDiagnostics.IsChecked != visible)
            {
                showDiagnostics.IsChecked = visible;
                showDiagnostics.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, showDiagnostics));
                DrainDispatcher();
            }
        }
    }

    private static T Find<T>(FrameworkElement owner, string name)
        where T : FrameworkElement
    {
        return owner.FindName(name) as T
            ?? throw new InvalidOperationException("Missing Events dock element " + name + ".");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void DrainDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }
}
