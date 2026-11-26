namespace Snoop.Host.SettingsSmoke;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;
using Snoop.Data.Tree;
using Snoop.Localization;
using Snoop.Windows;

internal static class WorkbenchAutomationClientSmoke
{
    public static void VerifyEditedHeight(SnoopUI inspector)
    {
        RunClient(inspector, ReadEditedHeight);
        Console.WriteLine("PASS: Windows UI Automation locates the filtered TextBlock, selects its Height property and reads 48 through ValuePattern.");
    }

    public static void VerifyTreeModeSelector(SnoopUI inspector)
    {
        var originalLanguage = InspectorLocalization.Default.LanguageTag;
        try
        {
            SelectMode("en-US", "Tree mode", TreeType.Automation, "Automation");
            SelectMode("zh-CN", "树模式", TreeType.Logical, "逻辑");
            SelectMode("zh-CN", "树模式", TreeType.Visual, "可视");
        }
        finally
        {
            InspectorLocalization.Default.SetLanguage(originalLanguage);
        }

        Console.WriteLine("PASS: Windows UI Automation reads localized tree modes and selects each mode before a target is loaded.");

        void SelectMode(string language, string label, TreeType mode, string optionName)
        {
            InspectorLocalization.Default.SetLanguage(language);
            inspector.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            RunClient(inspector, (handle, report) =>
            {
                report("locating the tree mode selector");
                var window = AutomationElement.FromHandle(handle);
                var selector = Find(window, AutomationElement.AutomationIdProperty, "TreeTypeComboBox");
                if (selector.Current.Name != label)
                {
                    throw new InvalidOperationException($"Expected tree mode label '{label}', got '{selector.Current.Name}'.");
                }

                report("opening the tree mode list");
                var dropdown = (ExpandCollapsePattern)selector.GetCurrentPattern(ExpandCollapsePattern.Pattern);
                dropdown.Expand();
                try
                {
                    report("selecting the " + mode + " tree");
                    var option = Find(selector, AutomationElement.AutomationIdProperty, mode.ToString());
                    ((SelectionItemPattern)option.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                    var selection = (SelectionPattern)selector.GetCurrentPattern(SelectionPattern.Pattern);
                    var selected = selection.Current.GetSelection().Single();
                    if (option.Current.Name != optionName || selected.Current.AutomationId != mode.ToString() || selected.Current.Name != optionName)
                    {
                        throw new InvalidOperationException($"The {language} tree selector should expose '{optionName}' as its selected item.");
                    }
                }
                finally
                {
                    dropdown.Collapse();
                }
            });

            if (inspector.CurrentTreeType != mode || inspector.RootTreeItem is not null || inspector.CurrentSelection is not null)
            {
                throw new InvalidOperationException("Changing the tree mode before inspection must leave an empty workbench in the requested mode.");
            }
        }
    }

    private static void RunClient(SnoopUI inspector, Action<IntPtr, Action<string>> run)
    {
        var handle = new WindowInteropHelper(inspector).Handle;
        // UIA calls back onto the WPF dispatcher. Give the client an MTA thread and
        // keep the window pumping messages while the client walks its control view.
        var operation = "connecting to the workbench window";
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer(DispatcherPriority.Normal, inspector.Dispatcher) { Interval = TimeSpan.FromSeconds(45) };
        timeout.Tick += (_, _) => frame.Continue = false;
        var read = Task.Run(() => run(handle, step => Volatile.Write(ref operation, step)));
        read.ConfigureAwait(false).GetAwaiter().OnCompleted(() => frame.Continue = false);
        timeout.Start();
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timeout.Stop();
        }

        if (!read.IsCompleted)
        {
            throw new TimeoutException("The UI Automation client timed out " + Volatile.Read(ref operation) + ".");
        }

        read.GetAwaiter().GetResult();
    }

    private static void ReadEditedHeight(IntPtr handle, Action<string> report)
    {
        var window = AutomationElement.FromHandle(handle);
        report("finding the element tree");
        var tree = Find(window, AutomationElement.AutomationIdProperty, "Tree");
        report("finding the filtered TextBlock");
        var item = Find(tree, AutomationElement.AutomationIdProperty, "NameInputResultGroupB");
        report("selecting the filtered TextBlock");
        ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();

        report("finding the property grid");
        var grid = Find(window, AutomationElement.AutomationIdProperty, "ListView");
        report("finding and selecting the Height row");
        var height = Find(grid, AutomationElement.AutomationIdProperty, "Height");
        ((SelectionItemPattern)height.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        report("reading the Height value cell");
        var table = (GridPattern)grid.GetCurrentPattern(GridPattern.Pattern);
        if (table.Current.RowCount != 1)
        {
            throw new InvalidOperationException("The edited-only property grid must contain just Height.");
        }

        var valueCell = table.GetItem(0, 2); // Breakpoint, Name, Value
        var editor = Find(valueCell, AutomationElement.ControlTypeProperty, ControlType.Edit);
        var value = ((ValuePattern)editor.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
        if (value != "48")
        {
            throw new InvalidOperationException($"The Height editor exposes '{value}' to UI Automation, expected '48'.");
        }
    }

    private static AutomationElement Find(AutomationElement parent, AutomationProperty property, object value)
    {
        var condition = new AndCondition(Automation.ControlViewCondition, new PropertyCondition(property, value));
        if (parent.FindFirst(TreeScope.Descendants, condition) is { } element)
        {
            return element;
        }

        var children = parent.FindAll(TreeScope.Children, Condition.TrueCondition).Cast<AutomationElement>().Take(8)
            .Select(child => $"{child.Current.ClassName} '{child.Current.Name}' [{child.Current.AutomationId}]");
        throw new InvalidOperationException($"UI Automation could not find {property.ProgrammaticName} = {value} under '{parent.Current.Name}'. Children: {string.Join(", ", children)}.");
    }
}
