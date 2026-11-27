namespace Snoop.Host.SettingsSmoke;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
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

                SetHelperExitCode(0);
                SelectTarget();
                AppChooser.InspectCommand.Execute(null, chooser);
                VerifyAttempt(target.Id);
                Require(app.Windows.Cast<Window>().All(window => window == chooser || !window.IsVisible),
                    "A successful command after reselection must leave no failure dialog.");

                CheckDialog("en-US", "CloseButton", "Chooser.Close");
                Require(!picker.IsDropDownOpen && NoSelectedTarget(),
                    "Closing the error without restarting selection must leave both target actions disabled.");
                VerifyAttempt(target.Id);
                Console.WriteLine("PASS: the native attach error clears target actions, localizes its retry and close buttons, and retries only after explicit reselection.");

                void SelectTarget()
                {
                    picker.SelectedItem = SelectableTarget(picker, target.Id);
                    picker.IsDropDownOpen = false;
                    Require(AppChooser.InspectCommand.CanExecute(null, chooser), "The controlled target must enable Attach when selected.");
                }

                bool NoSelectedTarget() => picker.SelectedItem is null && chooser.WindowInfos.CurrentItem is null
                    && !AppChooser.InspectCommand.CanExecute(null, chooser)
                    && !AppChooser.MagnifyCommand.CanExecute(null, chooser);

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
                    var inspection = chooser.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            var dialog = app.Windows.Cast<Window>().Single(window => window != chooser && window.IsVisible);
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
                            failure = ex;
                            CloseOtherWindows();
                        }
                    }, DispatcherPriority.ApplicationIdle);
                    watchdog.Start();
                    try
                    {
                        AppChooser.InspectCommand.Execute(null, chooser);
                    }
                    finally
                    {
                        watchdog.Stop();
                        inspection.Abort();
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
                chooser.Close();
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

    private static void VerifyAttempt(int targetProcessId)
    {
        var observation = File.ReadAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ObservationFile));
        Require(observation.Length == 2 && observation[0] == targetProcessId.ToString(CultureInfo.InvariantCulture),
            "The test helper must receive exactly one request for the controlled target PID.");
        Require(!File.Exists(observation[1]), "The launcher must remove transient settings after each helper attempt.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
