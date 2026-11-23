namespace Snoop.Host.SettingsSmoke;

using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Snoop.Controls;
using Snoop.Infrastructure;
using Snoop.Localization;
using Snoop.Views.DebugListenerTab;

internal static class InspectorPropertyAndDebugSmoke
{
    public static void Verify()
    {
        var localization = InspectorLocalization.Default;
        var originalLanguage = localization.LanguageTag;
        try
        {
            VerifyPropertyInspector(localization);
            VerifyDebugListener(localization);
            Console.WriteLine("PASS: property and debug filter labels switch languages while filter selection, trace levels and regex matching stay intact");
        }
        finally
        {
            localization.SetLanguage(originalLanguage);
        }
    }

    private static void VerifyPropertyInspector(InspectorLocalization localization)
    {
        var userFilters = Snoop.Core.Settings.Default.UserDefinedPropertyFilterSets;
        var userFilter = new PropertyFilterSet { DisplayName = "Layout", Properties = new[] { "MyProperty" } };
        userFilters.Add(userFilter);
        try
        {
            localization.SetLanguage("zh-CN");
            var inspector = new PropertyInspector();
            var clearAfterDelve = Find<CheckBox>(inspector, "checkBoxClearAfterDelve");
            var filterCombo = Find<ComboBox>(inspector, "FilterSetCombo");
            var grid = Find<PropertyGrid2>(inspector, "PropertyGrid");
            var list = Find<ListView>(grid, "ListView");
            var nameHeading = (TextBlock)((GridView)list.View).Columns[1].Header;
            var filterSets = inspector.AllFilterSets;
            var defaultFilter = filterSets.Single(filter => filter.IsDefault);
            var editFilter = filterSets.Single(filter => filter.IsEditCommand);
            var layoutFilter = filterSets.Single(filter => filter.IsReadOnly && filter.Properties?.Contains("width") == true);
            var layoutRules = layoutFilter.Properties;

            RequireText(clearAfterDelve.Content as string, "深入查看后清空筛选", "Clear-after-delve caption");
            RequireText(nameHeading.Text, "名称", "Property grid name heading");
            RequireText(defaultFilter.DisplayName, "（默认）", "Default filter caption");
            RequireText(editFilter.DisplayName, "编辑筛选集…", "Edit filter command caption");
            RequireText(layoutFilter.DisplayName, "布局", "Layout filter caption");
            RequireText(inspector.DelvePath, "目标对象为 NULL", "Empty delve path");

            filterCombo.SetCurrentValue(ComboBox.SelectedItemProperty, layoutFilter);
            DrainDispatcher();
            Require(ReferenceEquals(inspector.SelectedFilterSet, layoutFilter), "layout filter was not selected");
            localization.SetLanguage("en-US");
            DrainDispatcher();

            RequireText(clearAfterDelve.Content as string, "Clear after delve", "Clear-after-delve caption");
            RequireText(nameHeading.Text, "Name", "Property grid name heading");
            RequireText(defaultFilter.DisplayName, "(Default)", "Default filter caption");
            RequireText(editFilter.DisplayName, "Edit Filters...", "Edit filter command caption");
            RequireText(layoutFilter.DisplayName, "Layout", "Layout filter caption");
            RequireText(inspector.DelvePath, "Target object is NULL", "Empty delve path");
            Require(ReferenceEquals(inspector.SelectedFilterSet, layoutFilter) && ReferenceEquals(filterCombo.SelectedItem, layoutFilter),
                "changing the language changed the selected filter");
            Require(ReferenceEquals(layoutFilter.Properties, layoutRules) && defaultFilter.IsDefault && editFilter.IsEditCommand,
                "changing the language changed a built-in filter's behavior");
            Require(inspector.AllFilterSets.Contains(userFilter) && userFilter.DisplayName == "Layout" && userFilter.Properties!.Single() == "MyProperty",
                "changing the language changed a user filter");
        }
        finally
        {
            userFilters.Remove(userFilter);
        }
    }

    private static void VerifyDebugListener(InspectorLocalization localization)
    {
        var traceSwitch = PresentationTraceSources.DataBindingSource.Switch;
        var originalLevel = traceSwitch.Level;
        try
        {
            localization.SetLanguage("zh-CN");
            using var listener = new DebugListenerControl();
            var model = (FiltersViewModel)listener.DataContext;
            model.ClearFilters();
            var status = Find<TextBlock>(listener, "textBlockStatus");
            var traceLevels = Find<ComboBox>(listener, "comboBoxPresentationTraceLevel");
            var errorLevel = (ComboBoxItem)traceLevels.Items[2];
            RequireText(status.Text, "筛选已关闭", "Inactive filter status");
            RequireText(((TextBlock)errorLevel.Content).Text, "错误", "Trace-level label");
            RequireText(errorLevel.Tag as string, "Error", "Trace-level tag");

            var filter = new SnoopSingleFilter { FilterType = FilterType.RegularExpression, Text = "^trace:[0-9]+$" };
            model.InitializeFilters(new[] { filter });
            // An empty dialog session closes without a save prompt. The template
            // and listener each exercise a separate filter below.
            var dialog = new SetFiltersWindow(new FiltersViewModel(Array.Empty<SnoopSingleFilter>()));
            try
            {
                var apply = (TextBlock)Find<Button>(dialog, "buttonSetFilter").Content;
                var output = Find<TextBox>(listener, "textBoxDebugContent");
                var filterRow = (StackPanel)Find<ListBox>(dialog, "listBoxFilters").ItemTemplate.LoadContent();
                filterRow.DataContext = new SnoopSingleFilter { IsInverse = true, FilterType = FilterType.RegularExpression, Text = "^X$" };
                var inverse = (TextBlock)filterRow.Children[0];
                var matchType = (ComboBox)filterRow.Children[1];
                var pattern = (TextBox)filterRow.Children[2];
                traceLevels.SelectedItem = errorLevel;
                listener.Write("ignored");
                listener.Write("Trace:42");
                DrainDispatcher();
                RequireText(dialog.Title, "设置筛选器", "Debug filter dialog title");
                RequireText(apply.Text, "应用筛选器", "Apply filter caption");
                RequireText(status.Text, "筛选已启用", "Active filter status");
                RequireText(inverse.Text, "非", "Inverse filter badge");
                RequireText(((TextBlock)((ComboBoxItem)matchType.SelectedItem).Content).Text, "正则表达式", "Filter match type");
                RequireText(output.Text, "Trace:42", "Matched debug output");
                Require(traceSwitch.Level == SourceLevels.Error, "trace-level selection did not apply Error");

                localization.SetLanguage("en-US");
                listener.Write("ignored");
                listener.Write("Trace:43");
                DrainDispatcher();
                RequireText(dialog.Title, "Set Filters", "Debug filter dialog title");
                RequireText(apply.Text, "Set Filter", "Apply filter caption");
                RequireText(status.Text, "Filter is ON", "Active filter status");
                RequireText(inverse.Text, "NOT", "Inverse filter badge");
                RequireText(((TextBlock)((ComboBoxItem)matchType.SelectedItem).Content).Text, "Regular Expression", "Filter match type");
                Require(matchType.SelectedIndex == 3 && pattern.Text == "^X$", "changing the language changed the filter editor value");
                RequireText(((TextBlock)errorLevel.Content).Text, "Error", "Trace-level label");
                RequireText(output.Text, "Trace:42Trace:43", "Matched debug output");
                Require(traceSwitch.Level == SourceLevels.Error && ReferenceEquals(traceLevels.SelectedItem, errorLevel),
                    "changing the language changed the selected trace level");
                Require(filter.FilterType == FilterType.RegularExpression && filter.Text == "^trace:[0-9]+$",
                    "changing the language changed the debug filter pattern");
            }
            finally
            {
                dialog.Close();
            }
        }
        finally
        {
            traceSwitch.Level = originalLevel;
        }
    }

    private static T Find<T>(FrameworkElement parent, string name)
        where T : class
    {
        return parent.FindName(name) as T
               ?? throw new InvalidOperationException("Missing inspector control " + name);
    }

    private static void DrainDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    private static void RequireText(string? actual, string expected, string label)
    {
        Require(string.Equals(actual, expected, StringComparison.Ordinal), $"{label} expected '{expected}', got '{actual}'");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
