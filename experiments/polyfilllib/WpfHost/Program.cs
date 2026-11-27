namespace SnoopNext.PolyfillWpfHost;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool interactive = args.Length == 1 && args[0] == "--interactive";
        if (args.Length != 0 && !interactive)
        {
            Console.Error.WriteLine("Usage: WpfHost.exe [--interactive]");
            return 2;
        }

        var domain = AppDomain.CurrentDomain;
        domain.AssemblyLoad += (_, e) =>
        {
            if (e.LoadedAssembly.GetName().Name == "PolyfillLib")
            {
                Console.WriteLine("PolyfillLib AssemblyLoad: " + Describe(e.LoadedAssembly));
            }
        };
        var entryPolyfill = domain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "PolyfillLib");
        Console.WriteLine("PolyfillLib at Main entry: " + (entryPolyfill == null ? "absent" : Describe(entryPolyfill)));

        int result = 2;
        var app = new Application();
        var output = new TextBox { IsReadOnly = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var run = new Button { Content = "Run check", IsEnabled = interactive };
        var panel = new DockPanel();
        DockPanel.SetDock(run, Dock.Top);
        panel.Children.Add(run);
        panel.Children.Add(output);
        using var process = Process.GetCurrentProcess();
        var window = new Window { Title = $"SN-003 WPF host | PID {process.Id}", Content = panel, Width = 850, Height = 460 };

        void Log(string message)
        {
            Console.WriteLine(message);
            output.AppendText(message + Environment.NewLine);
        }

        void Check()
        {
            var snoop = domain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Snoop.Core");
            Log($"PID={process.Id}; AppDomain={domain.Id} ({domain.FriendlyName}); CLR={Environment.Version}; x64={Environment.Is64BitProcess}");
            Log("WPF: " + typeof(Window).Assembly.FullName + " @ " + typeof(Window).Assembly.Location);
            Log("Snoop.Core at start: " + (snoop == null ? "absent" : Describe(snoop)));
            Log("Fixture: " + Describe(typeof(Program).Assembly));
            if (interactive && snoop == null)
            {
                Log("Attach Snoop to this fixture and select Run check again.");
                return;
            }

            run.IsEnabled = false;
            try
            {
                CheckDependency(Log);
                window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    result = 0;
                    Log("PASS: fixture loader, dependency identity and subsequent WPF Dispatcher callback");
                    if (!interactive)
                    {
                        app.Shutdown();
                    }
                }));
            }
            catch (Exception ex)
            {
                result = 1;
                Log("FAIL: " + ex);
                if (ex is ReflectionTypeLoadException loadError)
                {
                    foreach (var detail in loadError.LoaderExceptions)
                    {
                        Log("LOADER: " + detail);
                    }
                }

                if (!interactive)
                {
                    app.Shutdown();
                }
            }
        }

        run.Click += (_, _) => Check();
        window.Loaded += (_, _) =>
        {
            if (interactive)
            {
                Log("Attach Snoop to the PID in this window title, then select Run check.");
            }
            else
            {
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Check));
            }
        };
        app.Run(window);
        return result;
    }

    private static void CheckDependency(Action<string> log)
    {
        var domain = AppDomain.CurrentDomain;
        string baseDir = domain.BaseDirectory;
        var early = domain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "PolyfillLib");
        if (early != null)
        {
            throw new InvalidOperationException("PolyfillLib was already loaded: " + Describe(early) + ". Start a fresh fixture.");
        }

        if (typeof(Program).Assembly.GetReferencedAssemblies().Any(a => a.Name == "PolyfillLib"))
        {
            throw new InvalidOperationException("The fixture has a compile-time PolyfillLib assembly reference.");
        }

#if NETFRAMEWORK
        if (Environment.Is64BitProcess)
        {
            throw new InvalidOperationException("Expected a 32-bit CLR4 host.");
        }

        log("Framework Release=" + Microsoft.Win32.Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", "unknown"));
        const string legacyRequest = "System.Memory, Version=4.0.1.2, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51";
        var expectedFile = Path.Combine(baseDir, "System.Memory.dll");
        var expected = AssemblyName.GetAssemblyName(expectedFile);
        string policy = domain.ApplyPolicy(legacyRequest);
        log("Host request: " + legacyRequest + "; CLR policy: " + policy);
        if (!string.Equals(policy, expected.FullName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The host binding redirect did not select its deployed System.Memory.");
        }

        var preloaded = Assembly.Load(legacyRequest);
        if (!string.Equals(preloaded.Location, expectedFile, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The host resolved System.Memory outside its application directory: " + preloaded.Location);
        }
#else
#if NET6_0
        const int expectedMajor = 6;
#else
        const int expectedMajor = 10;
#endif
        if (!Environment.Is64BitProcess || Environment.Version.Major != expectedMajor)
        {
            throw new InvalidOperationException($"Expected a 64-bit .NET {expectedMajor} host.");
        }

        var preloaded = Assembly.Load(new AssemblyName("System.Memory"));
#endif
        log("Preloaded: " + Describe(preloaded));
        var polyfillFile = Path.Combine(baseDir, "PolyfillLib.dll");
        var polyfill = Assembly.LoadFrom(polyfillFile);
        if (!string.Equals(polyfill.Location, polyfillFile, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unexpected PolyfillLib path: " + polyfill.Location);
        }

        log("Polyfill: " + Describe(polyfill));
        var requested = polyfill.GetReferencedAssemblies().Single(a => a.Name == "System.Memory");
        var resolved = Assembly.Load(requested);
        log("Polyfill metadata requests: " + requested.FullName);
        log("Resolved: " + Describe(resolved));
        if (!ReferenceEquals(preloaded, resolved))
        {
            throw new InvalidOperationException("The host and PolyfillLib reference resolved to different assembly instances.");
        }

        var types = polyfill.GetTypes();
        if (types.Length == 0)
        {
            throw new InvalidOperationException("PolyfillLib did not expose any types.");
        }

        log("Types: " + types.Length);
    }
    private static string Describe(Assembly assembly)
    {
        string path = assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location)
            ? "(no file location)"
            : Path.GetFullPath(assembly.Location);
        string identity = assembly.FullName + " @ " + path;
#if !NETFRAMEWORK
        var context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assembly);
        identity += " | LoadContext=" + context;
#endif
        return identity;
    }

}
