namespace Snoop.Host.SettingsSmoke;

using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Snoop.Localization;
using Snoop.Views.DebugListenerTab;
using Snoop.Windows;

internal static class WorkbenchOutputDockSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        if (inspector.FindName("DiagnosticsWorkbenchPane") is not TabControl diagnostics
            || inspector.FindName("WorkbenchOutputTab") is not TabItem output
            || !diagnostics.Items.Contains(output))
        {
            throw new InvalidOperationException("The Output tab must be in the diagnostics dock.");
        }

        var chrome = Find<DockPanel>(inspector, "WorkbenchChrome");
        var properties = Find<TabControl>(inspector, "PropertiesWorkbenchPane");
        var eventsTab = Find<TabItem>(inspector, "WorkbenchEventsTab");
        var showDiagnostics = Find<MenuItem>(inspector, "ShowDiagnosticsPaneItem");
        var listener = Find<DebugListenerControl>(inspector, "debugListenerControl");
        var textbox = Find<TextBox>(listener, "textBoxDebugContent");
        var search = Find<TextBox>(listener, "OutputSearchBox");
        var label = Find<Label>(listener, "OutputSearchLabel");
        var clear = Find<Button>(listener, "buttonClear");
        var filtersMenu = Find<Menu>(listener, "OutputFiltersMenu");
        var filters = (FiltersViewModel)listener.DataContext;
        Require(!properties.Items.Contains(output) && ReferenceEquals(output.Content, listener)
                && diagnostics.Items.IndexOf(output) == diagnostics.Items.IndexOf(eventsTab) + 1,
            "The Output tab must reuse the debug listener after Events in the diagnostics dock.");
        Require(ReferenceEquals(label.Target, search), "The output search label must identify its text box.");
        Require(!filtersMenu.IsMainMenu, "The Output filters menu must leave Alt and F10 activation to the workbench menu.");

        var savedLanguage = InspectorLocalization.Default.LanguageTag;
        var savedTab = diagnostics.SelectedItem;
        var savedVisibility = showDiagnostics.IsChecked;
        var savedQuery = search.Text;
        var savedFilters = filters.Filters.OfType<SnoopSingleFilter>().ToArray();
        var savedFilterEnabled = filters.IsSet;

        try
        {
            SetDiagnosticsVisible(true);
            diagnostics.SelectedItem = output;
            InspectorLocalization.Default.SetLanguage("en-US");
            SetSearch(string.Empty);
            Require(label.Content as string == "Search output", "Missing English output search label.");
            Clear();

            filters.ClearFilters();
            filters.InitializeFilters(new[] { new SnoopSingleFilter { FilterType = FilterType.RegularExpression, Text = "^trace:" } });
            const string first = "Trace: [Bracket]";
            var second = "Trace: other" + Environment.NewLine;
            var later = "Trace: [BRACKET] later" + Environment.NewLine;
            var hidden = "Trace: hidden" + Environment.NewLine;
            listener.Write(first);
            DrainDispatcher();
            VerifyOutput(first);
            VerifyMinimumLayout();

            listener.Write(second);
            listener.Write("Excluded: [bracket]" + Environment.NewLine);
            DrainDispatcher();
            VerifyOutput(first + second);
            SetSearch("  [bRaCkEt]  ");
            VerifyOutput(first);

            InspectorLocalization.Default.SetLanguage("zh-CN");
            diagnostics.SelectedItem = eventsTab;
            SetDiagnosticsVisible(false);
            SetDiagnosticsVisible(true);
            Require(ReferenceEquals(diagnostics.SelectedItem, eventsTab), "Restoring the diagnostics dock changed its active tab.");
            diagnostics.SelectedItem = output;
            DrainDispatcher();
            Require(label.Content as string == "搜索输出" && search.Text == "  [bRaCkEt]  ",
                "The Output search must retain its query across tab, visibility and language changes.");
            VerifyOutput(first);
            VerifyMinimumLayout();

            listener.Write(later);
            listener.Write(hidden);
            DrainDispatcher();
            VerifyOutput(first + later);
            SetSearch("no fixture output matches this");
            VerifyOutput(string.Empty);
            SetSearch(string.Empty);
            VerifyOutput(first + second + later + hidden);

            SetSearch("[bracket]");
            WriteOnWorker("Trace: [bracket] pending before Clear");
            Clear();
            VerifyOutput(string.Empty);
            SetSearch(string.Empty);
            VerifyOutput(string.Empty);

            const string after = "Trace: worker after Clear";
            WriteOnWorker(after);
            DrainDispatcher();
            VerifyOutput(after);
        }
        finally
        {
            Clear();
            filters.ClearFilters();
            filters.InitializeFilters(savedFilters);
            filters.IsSet = savedFilterEnabled;
            SetSearch(savedQuery);
            diagnostics.SelectedItem = savedTab;
            SetDiagnosticsVisible(savedVisibility);
            InspectorLocalization.Default.SetLanguage(savedLanguage);
            DrainDispatcher();
        }

        Console.WriteLine("PASS: diagnostics output preserves trace chunks, literal search and concurrent clear");

        void SetSearch(string query)
        {
            search.Text = query;
            DrainDispatcher();
        }

        void Clear()
        {
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, clear));
            DrainDispatcher();
        }

        void VerifyOutput(string expected)
        {
            Require(textbox.Text == expected,
                $"Unexpected captured output for query '{search.Text}'. Expected '{expected}', got '{textbox.Text}'.");
        }

        void WriteOnWorker(string message)
        {
            Exception? error = null;
            var worker = new Thread(() =>
            {
                try
                {
                    listener.Write(message);
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            }) { IsBackground = true };
            worker.Start();
            Require(worker.Join(TimeSpan.FromSeconds(5)), "A worker trace write must not wait for the inspector Dispatcher.");
            if (error is not null)
            {
                throw new InvalidOperationException("A worker could not write to the Output tab.", error);
            }
        }

        void VerifyMinimumLayout()
        {
            var frame = SystemParameters.WindowNonClientFrameThickness;
            var width = inspector.MinWidth - frame.Left - frame.Right;
            var height = inspector.MinHeight - frame.Top - frame.Bottom;
            chrome.Measure(new Size(width, height));
            chrome.Arrange(new Rect(0, 0, width, height));
            DrainDispatcher();

            Require(search.ActualWidth > 16 && search.ActualHeight > 0 && clear.ActualWidth > 0 && clear.ActualHeight > 0
                    && clear.TranslatePoint(new Point(clear.ActualWidth, 0), listener).X <= listener.ActualWidth + 1,
                "The Output dock must keep search and Clear available at minimum width.");
            Require(textbox.ExtentHeight > 0 && textbox.ExtentHeight <= textbox.ViewportHeight + 1
                    && textbox.TranslatePoint(new Point(0, textbox.ActualHeight), chrome).Y <= height + 1,
                $"The Output dock must fit a complete line at minimum height. Text extent {textbox.ExtentHeight}, viewport {textbox.ViewportHeight}.");
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
            ?? throw new InvalidOperationException("Missing Output dock element " + name + ".");
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
