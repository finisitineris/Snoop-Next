namespace Snoop.Host.SettingsSmoke;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Snoop.Next.Localization;
using static Snoop.Host.SettingsSmoke.PropertyOriginFilterSmoke;

internal static class LauncherSearchSmoke
{
    internal const string FixtureArgument = "--launcher-search-window";
    private const string FixtureTitle = "Ledger sample window";

    internal static int RunFixture()
    {
        var window = new Window { Title = FixtureTitle, Width = 240, Height = 100, ShowActivated = false };
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timeout.Tick += (_, _) => window.Close();
        timeout.Start();
        window.ShowDialog();
        timeout.Stop();
        return 0;
    }

    internal static void Verify(AppChooser chooser, ComboBox list)
    {
        using var current = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(current.MainModule.FileName, FixtureArgument) { UseShellExecute = false, CreateNoWindow = true };
        using var first = Process.Start(start) ?? throw new InvalidOperationException("Could not start the first WPF window.");
        try
        {
            using var second = Process.Start(start) ?? throw new InvalidOperationException("Could not start the second WPF window.");
            try
            {
                Await(() => HasWindow(first) && HasWindow(second), "The two discovery fixtures did not open their WPF windows.");
                VerifyPicker(chooser, list, first, second);
            }
            finally
            {
                Stop(second);
            }
        }
        finally
        {
            Stop(first);
        }

        static bool HasWindow(Process process)
        {
            process.Refresh();
            return !process.HasExited && process.MainWindowHandle != IntPtr.Zero && process.MainWindowTitle == FixtureTitle;
        }

        static void Stop(Process process)
        {
            if (!process.HasExited && (!process.CloseMainWindow() || !process.WaitForExit(2000)))
            {
                process.Kill();
                process.WaitForExit(2000);
            }
        }
    }

    private static void VerifyPicker(AppChooser chooser, ComboBox list, Process first, Process second)
    {
        // Host the real selector in a normal window to use its popup without installing the launcher's global hook.
        var host = new Window { DataContext = chooser, Width = 320, Height = 100, ShowInTaskbar = false };
        host.Resources.MergedDictionaries.Add(chooser.Resources);
        var parent = (Panel)list.Parent;
        var index = parent.Children.IndexOf(list);
        var text = LocalizationManager.Default;
        var originalLanguage = text.LanguageTag;
        try
        {
            parent.Children.RemoveAt(index);
            host.Content = list;
            host.Show();
            host.Activate();
            Require(list.Items.IsEmpty, "Initial discovery must start with an unpopulated picker.");
            var picker = UIElementAutomationPeer.CreatePeerForElement(list);
            var popup = (IExpandCollapseProvider)picker.GetPattern(PatternInterface.ExpandCollapse);
            popup.Expand();
            Await(() => HasBoth(), "Opening the picker must discover both running WPF fixture processes.");
            Console.WriteLine("PASS: opening an empty target picker discovers both live WPF fixture PIDs.");

            var search = Part<TextBox>("TargetSearchBox");
            var clear = Part<Button>("ClearTargetSearchButton");
            var empty = Part<TextBlock>("EmptyList");
            var popupRoot = (FrameworkElement)Part<Popup>("PART_Popup").Child;
            var pid = first.Id.ToString(CultureInfo.InvariantCulture);

            // Exercise normal WPF text input on the focused editor, including the text route through the combo box.
            popup.Collapse();
            Await(() => picker.GetChildren()?.Any(peer => peer.GetAutomationId() is "TargetSearchBox" or "ClearTargetSearchButton") != true,
                "The collapsed picker must omit its popup controls from the automation tree.");
            ApplicationCommands.Find.Execute(null, chooser);
            Await(() => list.IsDropDownOpen && search.IsKeyboardFocused, "The launcher Find command must open the popup and focus the search editor.");
            var entry = Child(AutomationControlType.Edit, "TargetSearchBox", "Chooser.SearchTargets");
            var reset = Child(AutomationControlType.Button, "ClearTargetSearchButton", "Chooser.ClearSearch");
            var input = (IValueProvider)entry.GetPattern(PatternInterface.Value);
            var invoke = (IInvokeProvider)reset.GetPattern(PatternInterface.Invoke);
            TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, search, pid));
            Await(() => search.Text == pid && list.Items.Count == 1 && ((WindowInfo)list.Items[0]).OwningProcessId == first.Id,
                "Keyboard PID input must leave only the matching target visible.");
            var row = (WindowInfo)list.Items[0];
            var item = picker.GetChildren().Single(peer => peer.GetAutomationControlType() == AutomationControlType.ListItem);
            Require(item.GetAutomationId() == "TargetProcess." + pid && item.GetName() == $"{FixtureTitle} - {first.ProcessName} [{pid}]",
                $"The target row must expose its title, process and PID. Got '{item.GetAutomationId()}', '{item.GetName()}'.");
            var select = (ISelectionItemProvider)item.GetPattern(PatternInterface.SelectionItem);
            select.Select();
            Require(select.IsSelected && ReferenceEquals(list.SelectedItem, row) && ReferenceEquals(chooser.WindowInfos.CurrentItem, row)
                    && AppChooser.InspectCommand.CanExecute(null, chooser) && AppChooser.MagnifyCommand.CanExecute(null, chooser),
                "Selecting the identified row must enable actions for that visible target.");

            input.SetValue(second.Id.ToString(CultureInfo.InvariantCulture));
            Await(() => list.Items.Count == 1 && ((WindowInfo)list.Items[0]).OwningProcessId == second.Id
                        && list.SelectedItem is null && chooser.WindowInfos.CurrentItem is null && !AppChooser.InspectCommand.CanExecute(null, chooser)
                        && !AppChooser.MagnifyCommand.CanExecute(null, chooser), "Hiding the selection must not switch either action to another matching process.");
            popup.Collapse();
            popup.Expand();
            Await(() => list.Items.Count == 1 && ((WindowInfo)list.Items[0]).OwningProcessId == second.Id && search.Text == second.Id.ToString(CultureInfo.InvariantCulture),
                "Reopening the picker must retain the active target query.");

            input.SetValue("  LEDGER SAMPLE  ");
            Await(() => HasBoth() && list.Items.Cast<WindowInfo>().All(v => v.WindowTitle.IndexOf("Ledger sample", StringComparison.OrdinalIgnoreCase) >= 0),
                "Case-insensitive title search must include both fixtures and exclude other titles.");
            input.SetValue("SETTINGSsmoke");
            Await(() => HasBoth() && list.Items.Cast<WindowInfo>().All(v => v.ProcessName?.IndexOf("settingssmoke", StringComparison.OrdinalIgnoreCase) >= 0),
                "Case-insensitive process-name search must include the fixtures and exclude unrelated processes.");

            foreach (var language in new[] { "en-US", "zh-CN" })
            {
                text.SetLanguage(language);
                input.SetValue("no-such-ledger-window");
                Await(() => list.Items.IsEmpty && empty.IsVisible && empty.Text == text["Chooser.NoMatches"], "Missing localized no-match state for " + language);
                Require(clear.IsVisible && clear.IsEnabled && search.IsVisible && entry.GetName() == text["Chooser.SearchTargets"]
                        && reset.GetName() == text["Chooser.ClearSearch"] && !input.IsReadOnly, "Missing accessible search or reset in " + language);
                Require(list.SelectedItem is null && chooser.WindowInfos.CurrentItem is null
                        && !AppChooser.InspectCommand.CanExecute(null, chooser) && !AppChooser.MagnifyCommand.CanExecute(null, chooser),
                    "A filtered-out target must not remain actionable.");
                if (language == "zh-CN")
                {
                    LauncherAppearanceSmoke.CaptureIfRequested(popupRoot, "launcher-target-search-empty-zh-CN");
                }

                invoke.Invoke();
                Await(() => search.Text.Length == 0 && !empty.IsVisible && HasBoth(), "Clearing the search must restore the original discovered targets.");
            }

            popup.Collapse();
            Console.WriteLine("PASS: launcher keyboard PID search, process/title filters, bilingual no-match state, command disabling and clear recovery through the picker automation tree.");

            AutomationPeer Child(AutomationControlType type, string id, string nameKey)
                => picker.GetChildren()?.SingleOrDefault(peer => peer.GetAutomationControlType() == type && peer.GetAutomationId() == id && peer.GetName() == text[nameKey])
                   ?? throw new InvalidOperationException($"The expanded picker does not expose its localized {type} '{id}'.");
        }
        finally
        {
            list.IsDropDownOpen = false;
            host.Content = null;
            host.Close();
            parent.Children.Insert(index, list);
            text.SetLanguage(originalLanguage);
        }

        bool HasBoth() => list.Items.Cast<WindowInfo>().Any(v => v.OwningProcessId == first.Id)
                       && list.Items.Cast<WindowInfo>().Any(v => v.OwningProcessId == second.Id);

        T Part<T>(string name)
            where T : FrameworkElement
            => list.Template.FindName(name, list) as T ?? throw new InvalidOperationException("Missing target picker control " + name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
