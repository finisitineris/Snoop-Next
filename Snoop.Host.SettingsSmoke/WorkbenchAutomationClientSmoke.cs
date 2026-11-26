namespace Snoop.Host.SettingsSmoke;

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;
using Snoop.Windows;

internal static class WorkbenchAutomationClientSmoke
{
    public static void VerifyEditedHeight(SnoopUI inspector)
    {
        var handle = new WindowInteropHelper(inspector).Handle;
        // UIA calls back onto the WPF dispatcher. Give the client an MTA thread and
        // keep the window pumping messages while the client walks its control view.
        var read = Task.Run(() => ReadEditedHeight(handle));
        var elapsed = Stopwatch.StartNew();
        while (!read.IsCompleted)
        {
            if (elapsed.Elapsed >= TimeSpan.FromSeconds(20))
            {
                throw new TimeoutException("The UI Automation client timed out reading the selected element's Height.");
            }

            inspector.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            Thread.Sleep(10);
        }

        read.GetAwaiter().GetResult();
        Console.WriteLine("PASS: Windows UI Automation locates the filtered TextBlock, selects its Height property and reads 48 through ValuePattern.");
    }

    private static void ReadEditedHeight(IntPtr handle)
    {
        var window = AutomationElement.FromHandle(handle);
        var tree = Find(window, AutomationElement.AutomationIdProperty, "Tree");
        var item = Find(tree, AutomationElement.AutomationIdProperty, "NameInputResultGroupB");
        ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();

        var grid = Find(window, AutomationElement.AutomationIdProperty, "ListView");
        var height = Find(grid, AutomationElement.AutomationIdProperty, "Height");
        ((SelectionItemPattern)height.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
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
