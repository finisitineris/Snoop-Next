namespace Snoop.Host.SettingsSmoke;

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Xml;
using Snoop.Infrastructure;
using Snoop.Windows;
using static Snoop.Host.SettingsSmoke.PropertyOriginFilterSmoke;

internal static class WorkbenchReturnToPickerSmoke
{
    internal const string FixtureArgument = "--workbench-return-fixture";
    private const string ScenarioDirectory = "workbench-return";
    private const string SnapshotRequest = "snapshot-request";
    private const string Snapshot = "attach-settings.xml";
    private const string OriginSettings = "origin-settings.xml";
    private const string OpenRequested = "open-requested";
    private const string ClosedNormally = "closed-normally";
    private const string DisconnectRequested = "disconnect-requested";
    private const string Disconnected = "disconnected";
    private const string StopRequested = "stop-requested";
    private const string TargetTitle = "Snoop picker return fixture";
    private static readonly TimeSpan fixtureTimeout = TimeSpan.FromSeconds(12);

    internal static void CopyRequestedSettings(string probeDirectory, string settingsPath)
    {
        var directory = Path.Combine(probeDirectory, ScenarioDirectory);
        if (File.Exists(Path.Combine(directory, SnapshotRequest)))
        {
            File.Copy(settingsPath, Path.Combine(directory, Snapshot), overwrite: true);
        }
    }

    internal static void Verify(AppChooser chooser, ComboBox picker, Action prepareHelper, Action<int> verifyAttempt)
    {
        var directory = Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ScenarioDirectory)).FullName;
        var initialSearch = chooser.TargetSearch;
        var initialState = chooser.WindowState;
        var executable = typeof(WorkbenchReturnToPickerSmoke).Assembly.Location;
        using var fixture = Process.Start(new ProcessStartInfo(executable, $"{FixtureArgument} \"{directory}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Could not start the separate WPF picker return fixture.");
        var output = fixture.StandardOutput.ReadToEndAsync();
        var errors = fixture.StandardError.ReadToEndAsync();
        try
        {
            Await(() =>
            {
                fixture.Refresh();
                return !fixture.HasExited && fixture.MainWindowHandle != IntPtr.Zero && fixture.MainWindowTitle == TargetTitle;
            }, "The separate picker return fixture must open a target window.", fixtureTimeout);
            var targetHandle = fixture.MainWindowHandle;
            var originToken = Attach(chooser, picker);
            var originalTargetRow = picker.SelectedItem;
            File.Move(Path.Combine(directory, Snapshot), Path.Combine(directory, OriginSettings));

            var otherChooser = new AppChooser { ShowActivated = false };
            try
            {
                otherChooser.Show();
                var otherPicker = (ComboBox)otherChooser.FindName("windowsComboBox");
                Require(Attach(otherChooser, otherPicker) != originToken,
                    "Distinct launchers must send distinct return tokens even when inspecting the same target.");
                otherChooser.WindowState = WindowState.Minimized;
                chooser.WindowState = WindowState.Minimized;
                Touch(directory, OpenRequested);
                Await(() => Exists(directory, ClosedNormally), "The separate fixture must close its first workbench normally.", fixtureTimeout);
                var ordinaryClose = Stopwatch.StartNew();
                Await(() =>
                {
                    Require(chooser.WindowState == WindowState.Minimized && !picker.IsDropDownOpen,
                        "Ordinary workbench Close must leave the origin chooser minimized with its picker closed.");
                    return ordinaryClose.Elapsed >= TimeSpan.FromMilliseconds(250);
                }, "Observe the origin chooser after ordinary Close.");

                Touch(directory, DisconnectRequested);
                Await(() => Exists(directory, Disconnected) && chooser.IsVisible && chooser.WindowState == WindowState.Normal
                            && picker.IsDropDownOpen && FindTarget(picker) is { } refreshedTargetRow
                            && !ReferenceEquals(originalTargetRow, refreshedTargetRow),
                    "Disconnect in the separate workbench must restore the originating launcher and reopen its refreshed picker.",
                    fixtureTimeout);
                Require(picker.SelectedItem is null && chooser.WindowInfos.CurrentItem is null
                        && !AppChooser.InspectCommand.CanExecute(null, chooser)
                        && !AppChooser.MagnifyCommand.CanExecute(null, chooser),
                    "Returning to the picker must require the user to select a target again.");
                Require(otherChooser.WindowState == WindowState.Minimized && !otherPicker.IsDropDownOpen
                        && otherPicker.SelectedItem is WindowInfo otherSelection && otherSelection.OwningProcessId == fixture.Id,
                    "The same-process launcher that did not originate this workbench must retain its selection and stay minimized.");
                fixture.Refresh();
                Require(!fixture.HasExited && fixture.MainWindowHandle == targetHandle,
                    "The separate target process and its original WPF window must remain open after disconnect.");
            }
            finally
            {
                otherChooser.Close();
            }
        }
        finally
        {
            picker.IsDropDownOpen = false;
            chooser.WindowState = initialState;
            chooser.TargetSearch = initialSearch;
            Touch(directory, StopRequested);
            if (!fixture.WaitForExit(2000))
            {
                fixture.Kill();
                fixture.WaitForExit(2000);
            }

            Console.Write(output.GetAwaiter().GetResult());
            Console.Error.Write(errors.GetAwaiter().GetResult());
        }

        Require(fixture.ExitCode == 0, $"The separate picker return fixture exited with code {fixture.ExitCode}.");
        Console.WriteLine("PASS: cross-process Disconnect restores only the originating launcher, refreshes its picker, and requires reselection.");

        WindowInfo? FindTarget(ComboBox targetPicker)
            => targetPicker.Items.OfType<WindowInfo>().FirstOrDefault(window => window.OwningProcessId == fixture.Id);

        Guid Attach(AppChooser launcher, ComboBox targetPicker)
        {
            launcher.TargetSearch = fixture.Id.ToString(CultureInfo.InvariantCulture);
            AppChooser.RefreshCommand.Execute(null, launcher);
            Await(() => FindTarget(targetPicker) is not null, "The launcher must discover the separate picker return fixture.", fixtureTimeout);
            targetPicker.SelectedItem = FindTarget(targetPicker);
            targetPicker.IsDropDownOpen = false;

            prepareHelper();
            Touch(directory, SnapshotRequest);
            try
            {
                AppChooser.InspectCommand.Execute(null, launcher);
                Await(() => !launcher.IsAttaching && Exists(directory, Snapshot),
                    "Successful attachment must send settings to the isolated helper.", fixtureTimeout);
            }
            finally
            {
                File.Delete(Path.Combine(directory, SnapshotRequest));
            }

            verifyAttempt(fixture.Id);
            using var current = Process.GetCurrentProcess();
            var document = new XmlDocument();
            document.Load(Path.Combine(directory, Snapshot));
            Require(document.DocumentElement?["LauncherProcessId"]?.InnerText == current.Id.ToString(CultureInfo.InvariantCulture),
                "The attachment settings must carry the originating launcher process ID.");
            Require(Guid.TryParse(document.DocumentElement?["LauncherReturnToken"]?.InnerText, out var token) && token != Guid.Empty,
                "The attachment settings must carry a nonempty originating launcher return token.");
            return token;
        }
    }

    internal static int RunFixture(string directory)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var target = new Window { Title = TargetTitle, Width = 240, Height = 100, ShowActivated = false };
        try
        {
            target.Show();
            Await(() => Exists(directory, OpenRequested) || Exists(directory, StopRequested),
                "The fixture must receive a request to open the managed workbench.", TimeSpan.FromSeconds(45));
            if (Exists(directory, StopRequested))
            {
                return 0;
            }

            Open().Close();
            Touch(directory, ClosedNormally);
            Await(() => Exists(directory, DisconnectRequested) || Exists(directory, StopRequested),
                "The fixture must receive a request to disconnect.", fixtureTimeout);
            if (Exists(directory, StopRequested))
            {
                return 0;
            }

            var inspector = Open();
            var item = (MenuItem)inspector.FindName("DisconnectTargetItem");
            var peer = UIElementAutomationPeer.CreatePeerForElement(item);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            Await(() => !inspector.IsVisible && inspector.RootObject is null && target.IsVisible,
                "Disconnect must clean up its workbench and keep the fixture window alive.");
            Touch(directory, Disconnected);
            Await(() => Exists(directory, StopRequested), "The fixture must receive a stop request after verification.", fixtureTimeout);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            app.Shutdown();
        }

        SnoopUI Open()
        {
            Require(SnoopManager.StartSnoop(Path.Combine(directory, OriginSettings)) == 0,
                "The managed attachment entry point must open the separate workbench.");
            return app.Windows.OfType<SnoopUI>().Single();
        }
    }

    private static bool Exists(string directory, string file) => File.Exists(Path.Combine(directory, file));

    private static void Touch(string directory, string file) => File.WriteAllText(Path.Combine(directory, file), string.Empty);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
