namespace Snoop;

using System;
using System.Diagnostics;
using System.Globalization;
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

    public AttachResult Snoop(IntPtr targetHwnd)
    {
        if (Application.Current?.CheckAccess() == true)
        {
            Mouse.OverrideCursor = Cursors.Wait;
        }

        try
        {
            InjectorLauncherManager.Launch(this, targetHwnd, typeof(SnoopManager).GetMethod(nameof(SnoopManager.StartSnoop))!, CreateTransientSettingsData(SnoopStartTarget.SnoopUI, targetHwnd));
        }
        catch (Exception e)
        {
            return new AttachResult(e);
        }
        finally
        {
            if (Application.Current?.CheckAccess() == true)
            {
                Mouse.OverrideCursor = null;
            }
        }

        return new AttachResult();
    }

    public AttachResult Magnify(IntPtr targetHwnd)
    {
        if (Application.Current?.CheckAccess() == true)
        {
            Mouse.OverrideCursor = Cursors.Wait;
        }

        try
        {
            InjectorLauncherManager.Launch(this, targetHwnd, typeof(SnoopManager).GetMethod(nameof(SnoopManager.StartSnoop))!, CreateTransientSettingsData(SnoopStartTarget.Zoomer, targetHwnd));
        }
        catch (Exception e)
        {
            return new AttachResult(e);
        }
        finally
        {
            if (Application.Current?.CheckAccess() == true)
            {
                Mouse.OverrideCursor = null;
            }
        }

        return new AttachResult();
    }

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
