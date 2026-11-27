namespace Snoop.Host.SettingsSmoke;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Snoop.Infrastructure;
using Snoop.Next.Localization;
using Snoop.Views;
using static Snoop.Host.SettingsSmoke.PropertyOriginFilterSmoke;

internal static class LauncherAttachFailureSmoke
{
    internal const string SmokeArgument = "--launcher-attach-smoke";
    internal const string ProbeArgument = "--launcher-attach-probe";
    private const string HelperPrefix = "Snoop.InjectorLauncher.";
    private const string ExitCodeFile = "helper-exit-code.txt";
    private const string ObservationFile = "helper-observation.txt";
    private const string HoldFile = "hold-attach-helper.txt";
    private const int HelperFailureExitCode = 5;

    internal static bool IsTestHelper()
        => Path.GetFileNameWithoutExtension(typeof(LauncherAttachFailureSmoke).Assembly.Location)
            .StartsWith(HelperPrefix, StringComparison.Ordinal);

    internal static int RunHelper(string[] args)
    {
        var target = Array.IndexOf(args, "--targetPID");
        var settings = Array.IndexOf(args, "--settingsFile");
        if (target < 0 || settings < 0 || target + 1 >= args.Length || settings + 1 >= args.Length
            || !File.Exists(args[settings + 1]))
        {
            return 125;
        }

        var directory = AppDomain.CurrentDomain.BaseDirectory;
        File.AppendAllLines(Path.Combine(directory, ObservationFile), new[] { args[target + 1], args[settings + 1] });
        var gate = Path.Combine(directory, HoldFile);
        var wait = Stopwatch.StartNew();
        while (File.Exists(gate) && wait.Elapsed < TimeSpan.FromSeconds(12))
        {
            Thread.Sleep(10);
        }

        return int.Parse(File.ReadAllText(Path.Combine(directory, ExitCodeFile)), CultureInfo.InvariantCulture);
    }

    internal static void Verify(string settingsRoot)
    {
        // Stage the toy helper and its host away from every real injector launcher.
        var directory = Directory.CreateDirectory(Path.Combine(settingsRoot, "attach-probe")).FullName;
        var executable = typeof(LauncherAttachFailureSmoke).Assembly.Location;
        foreach (var dependency in Directory.EnumerateFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll"))
        {
            File.Copy(dependency, Path.Combine(directory, Path.GetFileName(dependency)));
        }

        File.Copy(typeof(AppChooser).Assembly.Location, Path.Combine(directory, "Snoop.exe"));
        var stagedExecutable = Path.Combine(directory, Path.GetFileName(executable));
        File.Copy(executable, stagedExecutable);
        if (File.Exists(executable + ".config"))
        {
            File.Copy(executable + ".config", stagedExecutable + ".config");
        }

        var resources = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zh-CN", "Snoop.Next.Localization.resources.dll");
        var stagedResources = Directory.CreateDirectory(Path.Combine(directory, "zh-CN")).FullName;
        File.Copy(resources, Path.Combine(stagedResources, Path.GetFileName(resources)));

        using var current = Process.GetCurrentProcess();
        File.Copy(executable, Path.Combine(directory, $"{HelperPrefix}{NativeMethods.GetArchitecture(current)}.exe"));
        using var probe = Process.Start(new ProcessStartInfo(stagedExecutable, ProbeArgument)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Could not start the isolated launcher attach probe.");

        var output = probe.StandardOutput.ReadToEndAsync();
        var errors = probe.StandardError.ReadToEndAsync();
        if (!probe.WaitForExit(60000))
        {
            probe.Kill();
            probe.WaitForExit(2000);
            throw new InvalidOperationException("The isolated launcher attach probe timed out.");
        }

        Console.Write(output.GetAwaiter().GetResult());
        Console.Error.Write(errors.GetAwaiter().GetResult());
        Require(probe.ExitCode == 0, $"The isolated launcher attach probe exited with code {probe.ExitCode}.");
    }

    internal static int RunProbe()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            var target = new ProcessInfo(self);
            SetHelperExitCode(HelperFailureExitCode);
            var failed = target.Snoop(IntPtr.Zero);
            VerifyAttempt(self.Id);
            Require(!failed.Success && failed.AttachException is not null,
                "Attach must report failure when the helper process returns a nonzero exit code.");

            SetHelperExitCode(0);
            var succeeded = target.Snoop(IntPtr.Zero);
            VerifyAttempt(self.Id);
            Require(succeeded.Success, "A later attempt must succeed when the helper returns zero.");

            var pid = self.Id.ToString(CultureInfo.InvariantCulture);
            SetHelperExitCode(HelperFailureExitCode);
            Require(Snoop.Program.Main(new[] { "inspect", "--targetPID", pid }) == 1,
                "The Inspect CLI must report a nonzero exit status after helper failure.");
            VerifyAttempt(self.Id);
            SetHelperExitCode(HelperFailureExitCode);
            Require(Snoop.Program.Main(new[] { "magnify", "--targetPID", pid }) == 1,
                "The Magnify CLI must report a nonzero exit status after helper failure.");
            VerifyAttempt(self.Id);

            Console.WriteLine("PASS: launcher helper exit failures reach the attach result and CLI, with fresh transient settings on retry.");
            VerifyFailureDialog();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void VerifyFailureDialog()
    {
        var executable = typeof(LauncherAttachFailureSmoke).Assembly.Location;
        using var target = Process.Start(new ProcessStartInfo(executable, LauncherSearchSmoke.FixtureArgument)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start the WPF target for the attach dialog smoke.");
        try
        {
            Await(() =>
            {
                target.Refresh();
                return !target.HasExited && target.MainWindowHandle != IntPtr.Zero;
            }, "The WPF target for the attach dialog did not open.", TimeSpan.FromSeconds(20));

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Settings.Default.GlobalHotKey = null;
            var chooser = new AppChooser();
            try
            {
                VerifyNativeErrorGuidance();
                chooser.Show();
                var picker = (ComboBox)chooser.FindName("windowsComboBox");
                chooser.TargetSearch = target.Id.ToString(CultureInfo.InvariantCulture);
                AppChooser.RefreshCommand.Execute(null, chooser);
                Await(() => SelectableTarget(picker, target.Id) is not null,
                    "The launcher must discover the controlled WPF target.");

                CheckDialog("zh-CN", "RetryButton", "Chooser.Retry");
                Await(() => picker.IsDropDownOpen && SelectableTarget(picker, target.Id) is not null,
                    "Retry must return to the picker with the controlled target available for reselection.");
                Require(NoSelectedTarget(), "Retry must require explicit reselection before either command becomes available.");
                VerifyAttempt(target.Id);

                CheckProgress();

                CheckDialog("en-US", "CloseButton", "Chooser.Close");
                Require(!picker.IsDropDownOpen && NoSelectedTarget(),
                    "Closing the error without restarting selection must leave both target actions disabled.");
                VerifyAttempt(target.Id);
                Console.WriteLine("PASS: the native attach error clears target actions, localizes its retry and close buttons, and retries only after explicit reselection.");

                CheckCloseDuringAttempt();

                void SelectTarget()
                {
                    picker.SelectedItem = SelectableTarget(picker, target.Id);
                    picker.IsDropDownOpen = false;
                    Require(AppChooser.InspectCommand.CanExecute(null, chooser), "The controlled target must enable Attach when selected.");
                }

                bool NoSelectedTarget() => picker.SelectedItem is null && chooser.WindowInfos.CurrentItem is null
                    && !AppChooser.InspectCommand.CanExecute(null, chooser)
                    && !AppChooser.MagnifyCommand.CanExecute(null, chooser);

                void CheckProgress()
                {
                    var localization = LocalizationManager.Default;
                    localization.SetLanguage("zh-CN");
                    SetHelperExitCode(0);
                    SelectTarget();
                    var selected = picker.SelectedItem;
                    var gate = HoldHelper();

                    ProgressBar progress;
                    var inspectFinder = (WindowFinder)chooser.FindName("InspectWindowFinder");
                    var magnifyFinder = (WindowFinder)chooser.FindName("MagnifyWindowFinder");
                    try
                    {
                        AppChooser.InspectCommand.Execute(null, chooser);
                        Require(!AppChooser.InspectCommand.CanExecute(null, chooser),
                            "Attach must return while the held helper is still running and keep the target actions disabled.");
                        progress = chooser.FindName("AttachProgress") as ProgressBar
                            ?? throw new InvalidOperationException("The launcher must show an indeterminate attach progress bar.");
                        var status = chooser.FindName("AttachProgressStatus") as TextBlock
                            ?? throw new InvalidOperationException("The launcher must show a localized attach progress status.");

                        var dispatcherResponded = false;
                        chooser.Dispatcher.InvokeAsync(() => dispatcherResponded = true, DispatcherPriority.Background);
                        Await(() => dispatcherResponded && progress.IsVisible && progress.IsIndeterminate && status.IsVisible
                                    && status.Text == localization["Chooser.Attaching"] && AttemptIsWaiting(target.Id),
                            "The dispatcher and localized progress must remain active while the helper waits.", TimeSpan.FromSeconds(10));
                        var progressPeer = UIElementAutomationPeer.CreatePeerForElement(progress);
                        Require(progressPeer.GetAutomationControlType() == AutomationControlType.ProgressBar
                                && progressPeer.GetName() == localization["Chooser.Attaching"],
                            "UI Automation must discover the localized attach progress indicator.");

                        Require(!AppChooser.MagnifyCommand.CanExecute(null, chooser)
                                && !AppChooser.RefreshCommand.CanExecute(null, chooser)
                                && !AppChooser.SettingsCommand.CanExecute(null, chooser)
                                && !ApplicationCommands.Find.CanExecute(null, chooser)
                                && !picker.IsEnabled && !inspectFinder.IsEnabled && !magnifyFinder.IsEnabled,
                            "A running attempt must disable the picker, drag targets and commands that can change the target.");
                        AppChooser.InspectCommand.Execute(null, chooser);
                        AppChooser.MagnifyCommand.Execute(null, chooser);
                        AppChooser.RefreshCommand.Execute(null, chooser);
                        ApplicationCommands.Find.Execute(null, chooser);
                        Require(ReferenceEquals(picker.SelectedItem, selected) && !picker.IsDropDownOpen,
                            "A running attempt must keep its original selection and reject opening or refreshing the picker.");

                        localization.SetLanguage("en-US");
                        Await(() => status.IsVisible && status.Text == localization["Chooser.Attaching"] && AttemptIsWaiting(target.Id),
                            "The busy status must remain localized and only the original helper may be running.");
                        LauncherAppearanceSmoke.CaptureIfRequested((FrameworkElement)chooser.Content, "launcher-attach-progress");
                    }
                    finally
                    {
                        File.Delete(gate);
                    }

                    Await(() => !progress.IsVisible && AppChooser.InspectCommand.CanExecute(null, chooser)
                                && AppChooser.MagnifyCommand.CanExecute(null, chooser)
                                && AppChooser.RefreshCommand.CanExecute(null, chooser)
                                && AppChooser.SettingsCommand.CanExecute(null, chooser)
                                && ApplicationCommands.Find.CanExecute(null, chooser)
                                && picker.IsEnabled && inspectFinder.IsEnabled && magnifyFinder.IsEnabled,
                        "A successful helper exit must clear progress and restore the launcher actions.");
                    VerifyAttempt(target.Id);
                    Require(app.Windows.Cast<Window>().All(window => window == chooser || !window.IsVisible),
                        "A successful command after reselection must leave no failure dialog.");
                    Console.WriteLine("PASS: launcher attach progress stays responsive, prevents duplicate target actions, and clears when the helper exits.");
                }

                void CheckCloseDuringAttempt()
                {
                    SetHelperExitCode(0);
                    SelectTarget();
                    var gate = HoldHelper();
                    try
                    {
                        AppChooser.InspectCommand.Execute(null, chooser);
                        Await(() => AttemptIsWaiting(target.Id), "The launcher helper must start before requesting close.", TimeSpan.FromSeconds(10));
                        chooser.Close();
                        Require(chooser.IsVisible && AttemptIsWaiting(target.Id),
                            "Closing the launcher during attach must wait for the in-flight helper.");
                    }
                    finally
                    {
                        File.Delete(gate);
                    }

                    Await(() => !chooser.IsVisible, "The launcher must close after the in-flight helper exits.");
                    VerifyAttempt(target.Id);
                    Console.WriteLine("PASS: launcher close waits for the active helper and removes its transient settings.");
                }

                void CheckDialog(string language, string buttonName, string label)
                {
                    var localization = LocalizationManager.Default;
                    localization.SetLanguage(language);
                    SetHelperExitCode(HelperFailureExitCode);
                    SelectTarget();

                    Exception? failure = null;
                    var inspected = false;
                    var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                    watchdog.Tick += (_, _) =>
                    {
                        failure ??= new InvalidOperationException("The launcher error dialog did not close.");
                        CloseOtherWindows();
                        watchdog.Stop();
                    };
                    var inspection = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                    inspection.Tick += (_, _) =>
                    {
                        try
                        {
                            var dialog = app.Windows.Cast<Window>().SingleOrDefault(window => window != chooser && window.IsVisible);
                            if (dialog is null)
                            {
                                return;
                            }

                            inspection.Stop();
                            Require(NoSelectedTarget(), "The failed target must be cleared while error feedback is visible.");
                            var caption = (TextBlock)dialog.FindName("FailureCaption");
                            var guidance = (TextBlock)dialog.FindName("FailureGuidance");
                            Require(caption.Text.Contains(target.Id.ToString(CultureInfo.InvariantCulture))
                                    && guidance.Text == localization["Chooser.AttachFailureGuidance"],
                                "The error dialog must identify the failed target and provide recovery guidance.");
                            var button = (Button)dialog.FindName(buttonName);
                            var peer = UIElementAutomationPeer.CreatePeerForElement(button);
                            Require(peer.GetName() == localization[label], "The attach error action must have a localized automation name.");
                            LauncherAppearanceSmoke.CaptureIfRequested((FrameworkElement)dialog.Content, "launcher-attach-failure-" + language);
                            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                            inspected = true;
                        }
                        catch (Exception ex)
                        {
                            inspection.Stop();
                            failure = ex;
                            CloseOtherWindows();
                        }
                    };
                    watchdog.Start();
                    inspection.Start();
                    try
                    {
                        AppChooser.InspectCommand.Execute(null, chooser);
                        Await(() => (inspected || failure is not null)
                                    && app.Windows.Cast<Window>().All(window => window == chooser || !window.IsVisible),
                            "The native attach failure dialog must open and close.", TimeSpan.FromSeconds(20));
                    }
                    finally
                    {
                        watchdog.Stop();
                        inspection.Stop();
                    }

                    if (failure is not null)
                    {
                        throw new InvalidOperationException("The native attach failure dialog did not meet its acceptance checks.", failure);
                    }

                    Require(inspected, "An attach failure must show the native error dialog.");
                }

                void CloseOtherWindows()
                {
                    foreach (var window in app.Windows.Cast<Window>().Where(window => window != chooser).ToArray())
                    {
                        window.Close();
                    }
                }
            }
            finally
            {
                if (chooser.IsVisible)
                {
                    chooser.Close();
                }

                app.Shutdown();
            }
        }
        finally
        {
            if (!target.HasExited && (!target.CloseMainWindow() || !target.WaitForExit(2000)))
            {
                target.Kill();
                target.WaitForExit(2000);
            }
        }
    }

    private static WindowInfo? SelectableTarget(ComboBox picker, int processId)
        => picker.Items.Cast<WindowInfo>().SingleOrDefault(window => window.OwningProcessId == processId);

    private static void VerifyNativeErrorGuidance()
    {
        var localization = LocalizationManager.Default;
        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            localization.SetLanguage(language);
            Verify(5, "Chooser.AttachAccessDenied");
            Verify(1223, "Chooser.AttachElevationCanceled");
        }

        Console.WriteLine("PASS: Windows access denial and canceled elevation show their own guidance in Chinese and English.");

        void Verify(int errorCode, string guidanceKey)
        {
            var dialog = new AttachFailureDialog("Chooser.InspectFailed", "Test target", new Win32Exception(errorCode));
            try
            {
                var guidance = (TextBlock)dialog.FindName("FailureGuidance");
                var expected = localization[guidanceKey];
                Require(expected != guidanceKey && guidance.Text == expected,
                    $"Windows error {errorCode} must use the localized recovery message for {localization.LanguageTag}.");
            }
            finally
            {
                dialog.Close();
            }
        }
    }

    private static void SetHelperExitCode(int exitCode)
    {
        var directory = AppDomain.CurrentDomain.BaseDirectory;
        File.Delete(Path.Combine(directory, ObservationFile));
        File.WriteAllText(Path.Combine(directory, ExitCodeFile), exitCode.ToString(CultureInfo.InvariantCulture));
    }

    private static string HoldHelper()
    {
        var gate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, HoldFile);
        File.WriteAllText(gate, string.Empty);
        return gate;
    }

    private static void VerifyAttempt(int targetProcessId)
    {
        var observation = File.ReadAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ObservationFile));
        Require(observation.Length == 2 && observation[0] == targetProcessId.ToString(CultureInfo.InvariantCulture),
            "The test helper must receive exactly one request for the controlled target PID.");
        Require(!File.Exists(observation[1]), "The launcher must remove transient settings after each helper attempt.");
    }

    private static bool AttemptIsWaiting(int targetProcessId)
    {
        try
        {
            var lines = File.ReadAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ObservationFile));
            return lines.Length == 2 && lines[0] == targetProcessId.ToString(CultureInfo.InvariantCulture) && File.Exists(lines[1]);
        }
        catch (IOException)
        {
            return false;
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
