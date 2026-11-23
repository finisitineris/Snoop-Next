namespace Snoop.Host.SettingsSmoke;

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Snoop.Localization;
using Snoop.Windows;

internal static class WorkbenchCommandBarSmoke
{
    public static void Verify(SnoopUI inspector)
    {
        var chrome = Find<DockPanel>(inspector, "WorkbenchChrome");
        var header = Find<StackPanel>(inspector, "WorkbenchHeader");
        var menu = Find<Menu>(inspector, "WorkbenchMenuBar");
        var commands = inspector.FindName("WorkbenchCommandBar") as Border
            ?? throw new InvalidOperationException("Missing dedicated workbench command bar.");
        var treeType = Find<ComboBox>(inspector, "TreeTypeComboBox");
        var theme = Find<ComboBox>(inspector, "ThemeComboBox");
        var refresh = Find<Button>(inspector, "RefreshTreeButton");
        var palette = Find<Button>(inspector, "CommandPaletteButton");
        var status = Find<StatusBar>(inspector, "WorkbenchStatusBar");

        Require(chrome.Children.Contains(header) && DockPanel.GetDock(header) == Dock.Top
                && header.Orientation == Orientation.Vertical && header.Children.Count >= 2
                && ReferenceEquals(header.Children[0], menu) && ReferenceEquals(header.Children[1], commands),
            "The workbench must dock its menu and command bar in separate rows above the navigation panes.");

        var focusVisual = inspector.FindResource("SnoopNext.WorkbenchFocusVisual");
        foreach (var control in new Control[] { treeType, theme, refresh, palette })
        {
            Require(control.Focusable && control.IsTabStop && ReferenceEquals(control.FocusVisualStyle, focusVisual),
                control.Name + " must be keyboard reachable and use the workbench focus style.");
        }

        Require(ReferenceEquals(refresh.Command, SnoopUI.RefreshCommand)
                && SnoopUI.RefreshCommand.InputGestures.OfType<KeyGesture>()
                    .Any(gesture => gesture.Key == Key.F5 && gesture.Modifiers == ModifierKeys.None),
            "The command bar refresh must preserve the F5 refresh command.");

        var savedLanguage = InspectorLocalization.Default.LanguageTag;
        try
        {
            VerifyLanguage("en-US", "Refresh tree", "Refresh the tree view (F5)");
            VerifyLanguage("zh-CN", "刷新元素树", "刷新元素树 (F5)");
        }
        finally
        {
            InspectorLocalization.Default.SetLanguage(savedLanguage);
            DrainDispatcher();
        }

        Console.WriteLine("PASS: workbench command bar preserves localization, F5 refresh and narrow layout");

        void VerifyLanguage(string language, string caption, string expectedToolTip)
        {
            InspectorLocalization.Default.SetLanguage(language);
            DrainDispatcher();
            var frame = SystemParameters.WindowNonClientFrameThickness;
            var bounds = new Rect(0, 0, inspector.MinWidth - frame.Left - frame.Right,
                inspector.MinHeight - frame.Top - frame.Bottom);
            chrome.Measure(bounds.Size);
            chrome.Arrange(bounds);
            DrainDispatcher();

            var toolTip = refresh.ToolTip is TextBlock textBlock ? textBlock.Text : refresh.ToolTip as string;
            Require(refresh.Content as string == caption && toolTip == expectedToolTip,
                "Incorrect command bar caption or tool tip in " + language + ".");
            Require(FitsWithin(chrome, menu) && FitsWithin(chrome, commands)
                    && menu.Items.OfType<MenuItem>().All(item => FitsWithin(menu, item))
                    && FitsWithin(commands, treeType) && FitsWithin(commands, theme)
                    && FitsWithin(commands, refresh) && FitsWithin(commands, palette),
                "The menu and command bar controls must fit at the minimum window width in " + language + ".");
            var statusBottom = status.TranslatePoint(new Point(0, status.ActualHeight), chrome).Y;
            Require(status.ActualHeight > 0 && statusBottom <= bounds.Height + 1,
                $"The status bar must fit under the command bar at the minimum window height in {language}. "
                + $"Status bottom {statusBottom:F1} exceeds client height {bounds.Height:F1}.");
        }
    }

    private static bool FitsWithin(FrameworkElement ancestor, FrameworkElement element)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0 || !ancestor.IsAncestorOf(element))
        {
            return false;
        }

        var origin = element.TranslatePoint(new Point(0, 0), ancestor);
        return origin.X >= -1 && origin.Y >= -1
               && origin.X + element.ActualWidth <= ancestor.ActualWidth + 1
               && origin.Y + element.ActualHeight <= ancestor.ActualHeight + 1;
    }

    private static T Find<T>(FrameworkElement owner, string name)
        where T : FrameworkElement
    {
        return owner.FindName(name) as T
            ?? throw new InvalidOperationException("Missing workbench command bar element " + name + ".");
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
