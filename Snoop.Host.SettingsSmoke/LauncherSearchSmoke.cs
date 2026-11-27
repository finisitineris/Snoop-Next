namespace Snoop.Host.SettingsSmoke;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
        var fixtures = new List<Process>();
        try
        {
            var first = Start(current.MainModule.FileName, FixtureArgument);
            var second = Start(current.MainModule.FileName, FixtureArgument);
            var frameworkX86 = Start(FindFixture("net462"), $"\"{FixtureTitle}\"");
            var modern = Start(FindFixture("net10.0-windows"), $"\"{FixtureTitle}\"");
            Await(() => fixtures.All(HasWindow), "The discovery fixtures did not open their WPF windows.");
            VerifyPicker(chooser, list, first, second, frameworkX86, modern);
        }
        finally
        {
            foreach (var fixture in fixtures)
            {
                using (fixture)
                {
                    Stop(fixture);
                }
            }
        }

        Process Start(string path, string arguments)
        {
            var start = new ProcessStartInfo(path, arguments) { UseShellExecute = false, CreateNoWindow = true };
            var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start WPF fixture '{path}'.");
            fixtures.Add(process);
            return process;
        }

        static string FindFixture(string framework)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", framework, "Snoop.Host.SettingsSmoke.Fixture.exe");
            return File.Exists(path)
                ? path
                : throw new FileNotFoundException("Build Snoop.Host.SettingsSmoke.Fixture in the same configuration before running the smoke.", path);
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

    private static void VerifyPicker(AppChooser chooser, ComboBox list, Process first, Process second, Process frameworkX86, Process modern)
    {
        const int all = 0;
        const int netFramework = 1;
        const int coreClr = 2;
        const int x64 = 2;

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
            Await(() => HasAllFixtures(), "Opening the picker must discover all running WPF fixture processes.");
            Console.WriteLine("PASS: opening an empty target picker discovers live CLR4 and CoreCLR WPF fixture PIDs.");

            var search = Part<TextBox>("TargetSearchBox");
            var clear = Part<Button>("ClearTargetSearchButton");
            var empty = Part<TextBlock>("EmptyList");
            var popupRoot = (FrameworkElement)Part<Popup>("PART_Popup").Child;
            var pid = first.Id.ToString(CultureInfo.InvariantCulture);

            // Exercise normal WPF text input on the focused editor, including the text route through the combo box.
            popup.Collapse();
            Await(() => picker.GetChildren()?.Any(peer => peer.GetAutomationId() is "TargetSearchBox" or "ClearTargetSearchButton"
                    or "TargetRuntimeFilter" or "TargetArchitectureFilter") != true,
                "The collapsed picker must omit its popup controls from the automation tree.");
            ApplicationCommands.Find.Execute(null, chooser);
            Await(() => list.IsDropDownOpen && search.IsKeyboardFocused, "The launcher Find command must open the popup and focus the search editor.");
            var entry = Child(AutomationControlType.Edit, "TargetSearchBox", "Chooser.SearchTargets");
            var runtime = Child(AutomationControlType.ComboBox, "TargetRuntimeFilter", "Chooser.Runtime");
            var architecture = Child(AutomationControlType.ComboBox, "TargetArchitectureFilter", "Chooser.Architecture");
            var reset = Child(AutomationControlType.Button, "ClearTargetSearchButton", "Chooser.ClearFilters");
            var runtimeFilter = Part<ComboBox>("TargetRuntimeFilter");
            var architectureFilter = Part<ComboBox>("TargetArchitectureFilter");
            var input = (IValueProvider)entry.GetPattern(PatternInterface.Value);
            var invoke = (IInvokeProvider)reset.GetPattern(PatternInterface.Invoke);
            Require(runtimeFilter.SelectedIndex == all && architectureFilter.SelectedIndex == all,
                "The target picker's runtime and architecture filters must initially include all targets.");
            Require(search.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && runtimeFilter.IsKeyboardFocusWithin,
                "Tab navigation from an empty search editor must reach the runtime chooser.");
            PressKey(Key.F4);
            Await(() => runtimeFilter.IsDropDownOpen && list.IsDropDownOpen,
                "F4 must open the runtime choices inside the open target picker.");
            PressKey(Key.Down);
            PressKey(Key.Enter);
            Await(() => !runtimeFilter.IsDropDownOpen && list.IsDropDownOpen && runtimeFilter.SelectedIndex == netFramework,
                "Down and Enter must choose .NET Framework while keeping the target picker open.");
            invoke.Invoke();
            Await(() => runtimeFilter.SelectedIndex == all, "Clear filters must reset a runtime-only selection.");
            ApplicationCommands.Find.Execute(null, chooser);
            Await(() => search.IsKeyboardFocused, "Find must return keyboard focus to the search editor.");
            TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, search, pid));
            Await(() => search.Text == pid && list.Items.Count == 1 && ((WindowInfo)list.Items[0]).OwningProcessId == first.Id,
                "Keyboard PID input must leave only the matching target visible.");
            var row = (WindowInfo)list.Items[0];
            Await(() => VisibleRowIds().SequenceEqual(new[] { "TargetProcess." + pid }),
                "The picker automation tree must expose only the filtered PID after the row containers update.");
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
            var secondRowId = "TargetProcess." + second.Id.ToString(CultureInfo.InvariantCulture);
            Await(() => VisibleRowIds().SequenceEqual(new[] { secondRowId }),
                "Replacing the PID query must remove the previously selected process from the automation tree.");
            popup.Collapse();
            Require(Part<TextBlock>("Placeholder").Text == second.Id.ToString(CultureInfo.InvariantCulture),
                "The collapsed picker must show the active text query when no target is selected.");
            popup.Expand();
            Await(() => list.Items.Count == 1 && ((WindowInfo)list.Items[0]).OwningProcessId == second.Id && search.Text == second.Id.ToString(CultureInfo.InvariantCulture),
                "Reopening the picker must retain the active target query.");

            input.SetValue("  LEDGER SAMPLE  ");
            Await(() => HasAllFixtures() && list.Items.Cast<WindowInfo>().All(v => v.WindowTitle.IndexOf("Ledger sample", StringComparison.OrdinalIgnoreCase) >= 0),
                "Case-insensitive title search must include all fixtures and exclude other titles.");
            input.SetValue("SETTINGSsmoke");
            Await(() => HasAllFixtures() && list.Items.Cast<WindowInfo>().All(v => v.ProcessName?.IndexOf("settingssmoke", StringComparison.OrdinalIgnoreCase) >= 0),
                "Case-insensitive process-name search must include the fixtures and exclude unrelated processes.");

            foreach (var language in new[] { "en-US", "zh-CN" })
            {
                text.SetLanguage(language);
                input.SetValue("no-such-ledger-window");
                Await(() => list.Items.IsEmpty && empty.IsVisible && empty.Text == text["Chooser.NoMatches"], "Missing localized no-match state for " + language);
                Await(() => VisibleRowIds().Length == 0, "An empty result must expose no actionable process row to automation.");
                Require(clear.IsVisible && clear.IsEnabled && search.IsVisible && entry.GetName() == text["Chooser.SearchTargets"]
                        && runtimeFilter.IsVisible && runtime.GetName() == text["Chooser.Runtime"]
                        && architectureFilter.IsVisible && architecture.GetName() == text["Chooser.Architecture"]
                        && reset.GetName() == text["Chooser.ClearFilters"] && !input.IsReadOnly, "Missing accessible target filters in " + language);
                Require(list.SelectedItem is null && chooser.WindowInfos.CurrentItem is null
                        && !AppChooser.InspectCommand.CanExecute(null, chooser) && !AppChooser.MagnifyCommand.CanExecute(null, chooser),
                    "A filtered-out target must not remain actionable.");
                if (language == "zh-CN")
                {
                    LauncherAppearanceSmoke.CaptureIfRequested(popupRoot, "launcher-target-search-empty-zh-CN");
                }

                invoke.Invoke();
                Await(() => search.Text.Length == 0 && !empty.IsVisible && HasAllFixtures(), "Clearing the search must restore the original discovered targets.");
            }

            input.SetValue(FixtureTitle);
            runtimeFilter.SelectedIndex = netFramework;
            Await(() => Only(first, second, frameworkX86), "The .NET Framework filter must exclude the CoreCLR target and retain CLR4 targets of both architectures.");
            var expectedOrder = new[] { first.Id, second.Id, frameworkX86.Id }.OrderBy(id => id);
            Require(list.Items.Cast<WindowInfo>().Select(target => target.OwningProcessId).SequenceEqual(expectedOrder),
                "Combining text and runtime filters must preserve ascending PID order.");
            list.SelectedItem = list.Items.Cast<WindowInfo>().Single(target => target.OwningProcessId == first.Id);
            Require(AppChooser.InspectCommand.CanExecute(null, chooser) && AppChooser.MagnifyCommand.CanExecute(null, chooser),
                "Both actions must be available for the selected .NET Framework target.");

            var architectureMenu = (IExpandCollapseProvider)architecture.GetPattern(PatternInterface.ExpandCollapse);
            architectureMenu.Expand();
            Await(() => architectureFilter.IsDropDownOpen && architecture.GetChildren()?.Any(peer => peer.GetName() == "x86") == true,
                "The x86 architecture option must be reachable through UI Automation.");
            var x86Option = architecture.GetChildren().Single(peer => peer.GetName() == "x86");
            ((ISelectionItemProvider)x86Option.GetPattern(PatternInterface.SelectionItem)).Select();
            architectureMenu.Collapse();
            Await(() => Only(frameworkX86) && list.SelectedItem is null && chooser.WindowInfos.CurrentItem is null
                        && !AppChooser.InspectCommand.CanExecute(null, chooser) && !AppChooser.MagnifyCommand.CanExecute(null, chooser),
                "Filtering to x86 must keep the x86 CLR4 row and clear the hidden target and both actions.");

            runtimeFilter.SelectedIndex = coreClr;
            Await(() => list.Items.IsEmpty && empty.IsVisible && empty.Text == text["Chooser.NoMatches"] && VisibleRowIds().Length == 0,
                "The CoreCLR and x86 filters must intersect and expose no rows when neither fixture matches both.");

            architectureFilter.SelectedIndex = x64;
            Await(() => Only(modern) && VisibleRowIds().SequenceEqual(new[] { "TargetProcess." + modern.Id.ToString(CultureInfo.InvariantCulture) }),
                "The CoreCLR and x64 filters must expose only the modern WPF target.");
            input.SetValue(pid);
            Await(() => list.Items.IsEmpty && empty.IsVisible, "A PID query must still be restricted by the active runtime and architecture.");
            input.SetValue(string.Empty);
            Require(clear.IsVisible && clear.IsEnabled, "Clear filters must remain available when only runtime and architecture filters are active.");
            Require(picker.GetItemStatus() == text["Chooser.FiltersActive"],
                "The picker must announce active filters even when the text query is empty.");
            input.SetValue(FixtureTitle);

            popup.Collapse();
            popup.Expand();
            Await(() => search.Text == FixtureTitle && runtimeFilter.SelectedIndex == coreClr && architectureFilter.SelectedIndex == x64 && Only(modern),
                "Reopening the picker must retain the query, runtime and architecture filters.");
            invoke.Invoke();
            Await(() => search.Text.Length == 0 && runtimeFilter.SelectedIndex == all && architectureFilter.SelectedIndex == all
                        && !empty.IsVisible && HasAllFixtures(), "Clear filters must reset the query and both selectors together.");

            popup.Collapse();
            Console.WriteLine("PASS: launcher PID, process/title, runtime and architecture filters, bilingual no-match state, selection clearing and combined reset.");

            string[] VisibleRowIds() => picker.GetChildren()
                .Where(peer => peer.GetAutomationControlType() == AutomationControlType.ListItem)
                .Select(peer => peer.GetAutomationId())
                .ToArray();

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

        bool HasAllFixtures() => new[] { first, second, frameworkX86, modern }
            .All(process => list.Items.Cast<WindowInfo>().Any(target => target.OwningProcessId == process.Id));

        bool Only(params Process[] expected) => list.Items.Count == expected.Length
            && expected.All(process => list.Items.Cast<WindowInfo>().Any(target => target.OwningProcessId == process.Id));

        T Part<T>(string name)
            where T : FrameworkElement
            => list.Template.FindName(name, list) as T ?? throw new InvalidOperationException("Missing target picker control " + name);
    }

    private static void PressKey(Key key)
    {
        foreach (var routedEvent in new[] { Keyboard.PreviewKeyDownEvent, Keyboard.PreviewKeyUpEvent })
        {
            if (Keyboard.FocusedElement is not UIElement focus || PresentationSource.FromVisual(focus) is not { } source)
            {
                throw new InvalidOperationException($"No keyboard focus for {key}.");
            }

            InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = routedEvent,
                Source = focus
            });
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
