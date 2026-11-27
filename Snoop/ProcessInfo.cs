namespace Snoop;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Snoop.Data;
using Snoop.Infrastructure;
using Snoop.Next.Localization;

public class ProcessInfo
{
    private bool? isOwningProcessElevated;
    private TargetRuntime? targetRuntimes;
    private string? targetArchitecture;

    public ProcessInfo(int processId)
        : this(Process.GetProcessById(processId))
    {
    }

    public ProcessInfo(Process process)
    {
        this.Process = process;
    }

    public Process Process { get; }

    public bool IsProcessElevated => this.isOwningProcessElevated ??= NativeMethods.IsProcessElevated(this.Process);

    internal TargetRuntime TargetRuntimes => this.targetRuntimes ??= this.ReadTargetRuntimes();

    internal string TargetArchitecture => this.targetArchitecture ??= this.ReadTargetArchitecture();

    private TargetRuntime ReadTargetRuntimes()
    {
        var runtimes = TargetRuntime.None;
        foreach (var module in NativeMethods.GetModules(this.Process))
        {
            if (string.Equals(module.szModule, "coreclr.dll", StringComparison.OrdinalIgnoreCase))
            {
                runtimes |= TargetRuntime.CoreClr;
            }
            else if (string.Equals(module.szModule, "clr.dll", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(module.szModule, "mscorwks.dll", StringComparison.OrdinalIgnoreCase))
            {
                runtimes |= TargetRuntime.NetFramework;
            }
        }

        return runtimes;
    }

    private string ReadTargetArchitecture()
    {
        try
        {
            return NativeMethods.GetArchitecture(this.Process);
        }
        catch (Exception)
        {
            // A failed read (for example, an exited process) has no architecture to match.
            return string.Empty;
        }
    }

    public AttachResult Snoop(IntPtr targetHwnd) => this.Attach(targetHwnd, SnoopStartTarget.SnoopUI);

    public AttachResult Magnify(IntPtr targetHwnd) => this.Attach(targetHwnd, SnoopStartTarget.Zoomer);

    internal Task<AttachResult> AttachAsync(IntPtr targetHwnd, SnoopStartTarget startTarget)
    {
        try
        {
            // Capture mutable host settings and system language before leaving the dispatcher.
            var settings = CreateTransientSettingsData(startTarget, targetHwnd);
            var processId = this.Process.Id;
            var completion = new TaskCompletionSource<AttachResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = new Thread(() => completion.SetResult(AttachOnWorker(processId, targetHwnd, settings)))
            {
                Name = "Snoop_AttachWorker",
                IsBackground = true
            };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            return completion.Task;
        }
        catch (Exception ex)
        {
            return Task.FromResult(new AttachResult(ex));
        }
    }

    private static AttachResult AttachOnWorker(int processId, IntPtr targetHwnd, TransientSettingsData settings)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(targetHwnd, out var owningProcessId);
            if (owningProcessId != processId)
            {
                return new AttachResult(new InvalidOperationException(LocalizationManager.Default["Chooser.TargetUnavailable"]));
            }

            using var process = Process.GetProcessById(processId);
            new ProcessInfo(process).Launch(targetHwnd, settings);
            return new AttachResult();
        }
        catch (Exception ex)
        {
            return new AttachResult(ex);
        }
    }

    private AttachResult Attach(IntPtr targetHwnd, SnoopStartTarget startTarget)
    {
        var isUiThread = Application.Current?.CheckAccess() == true;
        if (isUiThread)
        {
            Mouse.OverrideCursor = Cursors.Wait;
        }

        try
        {
            this.Launch(targetHwnd, CreateTransientSettingsData(startTarget, targetHwnd));
        }
        catch (Exception e)
        {
            return new AttachResult(e);
        }
        finally
        {
            if (isUiThread)
            {
                Mouse.OverrideCursor = null;
            }
        }

        return new AttachResult();
    }

    private void Launch(IntPtr targetHwnd, TransientSettingsData settings)
        => InjectorLauncherManager.Launch(this, targetHwnd, typeof(SnoopManager).GetMethod(nameof(SnoopManager.StartSnoop))!, settings);

    private static TransientSettingsData CreateTransientSettingsData(SnoopStartTarget startTarget, IntPtr targetWindowHandle)
    {
        var settings = Settings.Default;

        return new TransientSettingsData
        {
            StartTarget = startTarget,
            TargetWindowHandle = targetWindowHandle.ToInt64(),

            MultipleAppDomainMode = settings.MultipleAppDomainMode,
            MultipleDispatcherMode = settings.MultipleDispatcherMode,
            SetOwnerWindow = settings.SetOwnerWindow,
            ShowActivated = settings.ShowActivated,
            EnableDiagnostics = settings.EnableDiagnostics,
            // Resolve "system" in the Snoop host. The injected process must
            // not infer its UI language from the inspected app's thread culture.
            LanguageTag = settings.LanguageTag == "system"
                ? LanguageTagPolicy.GetResourceLanguageTag(CultureInfo.CurrentUICulture)
                : settings.LanguageTag,
            ILSpyPath = settings.ILSpyPath
        };
    }
}
