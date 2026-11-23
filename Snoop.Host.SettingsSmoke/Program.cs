namespace Snoop.Host.SettingsSmoke;

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Snoop;
using Snoop.Data;
using Snoop.Localization;
using Snoop.Next.Localization;
using Snoop.Windows;

/// <summary>
/// Tests host language preferences and WPF controls without displaying windows
/// or attaching to another process. This can run on newer .NET Framework 4.x runtimes.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        const string settingsRootVariable = Snoop.Core.SettingsHelper.SNOOP_SETTINGS_PATH_ENV_VAR;
        var path = Path.Combine(Path.GetTempPath(), "SnoopHostSettings-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(path, "SnoopSettings.xml");
        var originalSettingsRoot = Environment.GetEnvironmentVariable(settingsRootVariable);
        try
        {
            // Seed both settings lookups before touching the settings singletons.
            var appSettings = Directory.CreateDirectory(Path.Combine(path, "AppSettings"));
            var inspectorFile = Path.Combine(appSettings.FullName, $"{Snoop.Core.EnvironmentEx.CurrentProcessName}.xml");
            File.WriteAllText(file, "<Settings />");
            File.WriteAllText(inspectorFile, "<Settings />");
            Environment.SetEnvironmentVariable(settingsRootVariable, path);
            return RunSmoke(file, inspectorFile);
        }
        finally
        {
            Environment.SetEnvironmentVariable(settingsRootVariable, originalSettingsRoot);
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (IOException)
            {
                // Test process still exits; temporary files are non-sensitive.
            }
        }
    }

    // Keep the JIT from initializing the settings singletons before Main sets the temporary root.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunSmoke(string file, string inspectorFile)
    {
        var originalCulture = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Require(Settings.Default.SettingsFile == file, "host settings did not use the temporary settings root");
            Require(Snoop.Core.Settings.Default.SettingsFile == inspectorFile,
                "inspector settings did not use the temporary settings root");

            AppDomainLanguageSmoke.Verify(Path.GetDirectoryName(file)!);

            var settings = new Settings { SettingsFile = file };
            Require(settings.LanguageTag == "zh-CN", "default language must be zh-CN");

            settings.LanguageTag = "en-US";
            settings.Save();
            var serialized = File.ReadAllText(file);
            Require(serialized.Contains("<LanguageTag>en-US</LanguageTag>"), "XML did not persist language");

            var loaded = new Settings { SettingsFile = file };
            loaded.Reload();
            Require(loaded.LanguageTag == "en-US", "reload did not restore en-US");

            loaded.LanguageTag = "system";
            loaded.Save();
            var systemLoaded = new Settings { SettingsFile = file };
            systemLoaded.Reload();
            Require(systemLoaded.LanguageTag == "system", "system preference did not persist or reload");

            loaded.LanguageTag = "invalid-value";
            Require(loaded.LanguageTag == "zh-CN", "invalid value did not normalize");
            loaded.Reset();
            Require(loaded.LanguageTag == "zh-CN", "reset did not restore default");

            // Simulate a legacy SnoopSettings.xml with no language field at all.
            var legacy = serialized.Replace("<LanguageTag>en-US</LanguageTag>", string.Empty);
            Require(legacy != serialized, "cannot construct legacy XML fixture");
            File.WriteAllText(file, legacy);
            var legacyLoaded = new Settings { SettingsFile = file };
            legacyLoaded.Reload();
            Require(legacyLoaded.LanguageTag == "zh-CN", "old XML without language did not load");

            File.WriteAllText(file, serialized.Replace(
                "<LanguageTag>en-US</LanguageTag>", "<LanguageTag>broken-value</LanguageTag>"));
            var invalidLoaded = new Settings { SettingsFile = file };
            invalidLoaded.Reload();
            Require(invalidLoaded.LanguageTag == "zh-CN", "invalid persisted XML value was not normalized");

            Require(Thread.CurrentThread.CurrentUICulture.Equals(originalCulture),
                "settings preference modified the thread UI culture");

            var committedHostLanguage = Settings.Default.LanguageTag;
            var committedManagerLanguage = LocalizationManager.Default.LanguageTag;
            var originalHostCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                var factory = typeof(ProcessInfo).GetMethod(
                    "CreateTransientSettingsData", BindingFlags.NonPublic | BindingFlags.Static);
                if (factory == null)
                {
                    throw new InvalidOperationException("ProcessInfo transient settings factory was not found");
                }

                Settings.Default.LanguageTag = "en-US";
                LocalizationManager.Default.SetLanguage("en-US");
                var hostPayload = factory.Invoke(null, new object[] { SnoopStartTarget.SnoopUI, IntPtr.Zero })
                    as TransientSettingsData
                    ?? throw new InvalidOperationException("ProcessInfo did not create transient settings data");
                Require(hostPayload.LanguageTag == "en-US",
                    "ProcessInfo did not copy the committed host LanguageTag into the injector payload");

                // The CLI does not run App.OnStartup to initialize the live manager.
                LocalizationManager.Default.SetLanguage("zh-CN");
                Settings.Default.LanguageTag = "system";
                var systemCases = new[]
                {
                    new { Culture = "de-DE", ExpectedTag = "en-US" },
                    new { Culture = "zh-TW", ExpectedTag = "zh-CN" }
                };
                foreach (var systemCase in systemCases)
                {
                    var culture = systemCase.Culture;
                    Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                    var systemPayload = factory.Invoke(null, new object[] { SnoopStartTarget.SnoopUI, IntPtr.Zero })
                        as TransientSettingsData
                        ?? throw new InvalidOperationException("ProcessInfo did not create system-language payload");
                    Require(systemPayload.LanguageTag == systemCase.ExpectedTag,
                        "ProcessInfo did not resolve the supported system language for " + culture);
                    Require(Thread.CurrentThread.CurrentUICulture.Name == culture,
                        "resolving system language modified the host thread CurrentUICulture");
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = originalHostCulture;
                Settings.Default.LanguageTag = committedHostLanguage;
                LocalizationManager.Default.SetLanguage(committedManagerLanguage);
            }

            // The host transports its committed language choice through the
            // existing transient XML channel used by the injector.
            var transient = new TransientSettingsData { LanguageTag = "en-US" };
            var transientPath = transient.WriteToFile();
            try
            {
                var transientXml = File.ReadAllText(transientPath);
                Require(transientXml.Contains("<LanguageTag>en-US</LanguageTag>"),
                    "transient settings XML did not include LanguageTag");

                var transientSerializer = new System.Xml.Serialization.XmlSerializer(typeof(TransientSettingsData));
                using (var stream = File.OpenRead(transientPath))
                {
                    var loadedTransient = transientSerializer.Deserialize(stream) as TransientSettingsData
                        ?? throw new InvalidOperationException("Transient settings could not be deserialized");
                    Require(loadedTransient.LanguageTag == "en-US", "transient language preference did not round-trip");
                }

                // Old host XML has no LanguageTag; the new injector side must
                // safely default to system rather than fail deserialization.
                var legacyTransientXml = transientXml.Replace("<LanguageTag>en-US</LanguageTag>", string.Empty);
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(legacyTransientXml)))
                {
                    var legacyTransient = transientSerializer.Deserialize(stream) as TransientSettingsData
                        ?? throw new InvalidOperationException("Legacy transient settings could not be deserialized");
                    Require(legacyTransient.LanguageTag == "system", "legacy transient XML did not default to system");
                }
            }
            finally
            {
                File.Delete(transientPath);
            }

            // Exercise the actual host settings view against a temporary
            // settings file. No real user profile settings are read or changed.
            var persistedSettingsPath = Settings.Default.SettingsFile;
            var persistedLanguage = Settings.Default.LanguageTag;
            var originalManagerLanguage = LocalizationManager.Default.LanguageTag;
            Settings.Default.SettingsFile = file;
            try
            {
                Settings.Default.LanguageTag = "zh-CN";
                Settings.Default.Save();
                LocalizationManager.Default.SetLanguage("zh-CN");

                // Instantiate the real host SettingsView on STA without showing a
                // window or attaching Snoop to any external process. This catches
                // runtime BAML, static resource, and selector wiring failures.
                var settingsView = new Snoop.Views.SettingsView();
                var languagePicker = FindRequired<System.Windows.Controls.ComboBox>(settingsView, "LanguageSelector");
                Require(languagePicker.Items.Count == 3, "expected zh-CN/en-US/system in host picker");
                Require(string.Equals(languagePicker.SelectedValue as string, Settings.Default.LanguageTag, StringComparison.Ordinal),
                    "host picker selection does not match the Snoop host setting");
                Require(Thread.CurrentThread.CurrentUICulture.Equals(originalCulture),
                    "constructing Snoop host SettingsView modified thread UI culture");
                Console.WriteLine("PASS: host SettingsView BAML loaded and language selector wired on STA");

                // Selecting a new language is a pending edit: no settings file
                // or live localization update occurs until Save is clicked.
                languagePicker.SelectedValue = "en-US";
                Require(Settings.Default.LanguageTag == "zh-CN", "language selection previewed into persisted settings");
                Require(LocalizationManager.Default.LanguageTag == "zh-CN", "language selection previewed live before Save");

                var discardButton = FindRequired<System.Windows.Controls.Button>(settingsView, "DiscardSettings");
                discardButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(languagePicker.SelectedValue as string == "zh-CN", "Discard did not restore persisted language");
                Require(LocalizationManager.Default.LanguageTag == "zh-CN", "Discard changed live language");

                languagePicker.SelectedValue = "en-US";
                var saveButton = FindRequired<System.Windows.Controls.Button>(settingsView, "SaveSettingsAndClose");
                saveButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(Settings.Default.LanguageTag == "en-US", "Save did not commit language setting");
                Require(LocalizationManager.Default.LanguageTag == "en-US", "Save did not apply live host language");
                Require(File.ReadAllText(file).Contains("<LanguageTag>en-US</LanguageTag>"), "Save did not persist selected language");

                // Reset changes the pending value to the default but does not
                // persist or apply it until Save is clicked.
                var resetView = new Snoop.Views.SettingsView();
                var resetPicker = FindRequired<System.Windows.Controls.ComboBox>(resetView, "LanguageSelector");
                var resetButton = FindRequired<System.Windows.Controls.Button>(resetView, "ResetAllSettings");
                resetButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(resetPicker.SelectedValue as string == "zh-CN", "Reset did not select default language");
                Require(Settings.Default.LanguageTag == "en-US", "Reset overwrote the committed language before Save");
                Require(LocalizationManager.Default.LanguageTag == "en-US", "Reset applied language before Save");
                Require(File.ReadAllText(file).Contains("<LanguageTag>en-US</LanguageTag>"), "Reset unexpectedly persisted before Save");

                var saveResetButton = FindRequired<System.Windows.Controls.Button>(resetView, "SaveSettingsAndClose");
                saveResetButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(LocalizationManager.Default.LanguageTag == "zh-CN", "Save after Reset did not apply default language");

                Console.WriteLine("PASS: SettingsView pending selection, Save, Discard and Reset behavior");

                // Exercise Snoop.Core's actual inspector XAML in this isolated
                // process. No other WPF application is attached or injected.
                var inspectorCulture = Thread.CurrentThread.CurrentUICulture;
                var inspectorSettingsPath = new TransientSettingsData { LanguageTag = "zh-CN" }.WriteToFile();
                try
                {
                    var loadedInspectorSettings = TransientSettingsData.LoadCurrent(inspectorSettingsPath);
                    InspectorLocalization.Default.InitializeFrom(loadedInspectorSettings);

                    var coreAssembly = typeof(SnoopUI).Assembly;
                    var resourceNames = coreAssembly.GetManifestResourceNames();
                    Require(Array.IndexOf(resourceNames, "Snoop.Localization.InspectorStrings.resources") >= 0,
                        "inspector strings were not embedded into Snoop.Core");
                    Require(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zh-CN", "Snoop.Core.resources.dll")),
                        "inspector localization unexpectedly emitted a satellite assembly");

                    var resourceManager = new System.Resources.ResourceManager(
                        "Snoop.Localization.InspectorStrings", coreAssembly);
                    var resourceSet = resourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, true);
                    if (resourceSet == null)
                    {
                        throw new InvalidOperationException("embedded inspector resource set was not found");
                    }

                    var resourceKeys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                    var resourceEnumerator = resourceSet.GetEnumerator();
                    while (resourceEnumerator.MoveNext())
                    {
                        var key = (string)resourceEnumerator.Key;
                        Require(resourceEnumerator.Value is string value && !string.IsNullOrWhiteSpace(value),
                            "empty or invalid inspector resource " + key);
                        resourceKeys.Add(key);
                    }

                    var englishKeyCount = 0;
                    foreach (var key in resourceKeys)
                    {
                        if (key.StartsWith("en-US.", StringComparison.Ordinal))
                        {
                            englishKeyCount++;
                            Require(resourceKeys.Contains("zh-CN." + key.Substring("en-US.".Length)),
                                "missing zh-CN inspector resource for " + key);
                        }
                    }

                    Require(englishKeyCount > 0 && resourceKeys.Count == englishKeyCount * 2,
                        "inspector resources must contain matching en-US and zh-CN key sets");
                    Console.WriteLine($"PASS: {englishKeyCount} non-empty inspector resource pairs");

                    var app = System.Windows.Application.Current
                        ?? new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                    Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
                    var inspector = new SnoopUI();
                    try
                    {
                        var fileMenu = FindRequired<System.Windows.Controls.MenuItem>(inspector, "FileMenuItem");
                        var treeHeader = FindRequired<System.Windows.Controls.Label>(inspector, "TreeHeaderLabel");
                        var inspectSnoop = FindRequired<System.Windows.Controls.MenuItem>(inspector, "InspectSnoopMenuItem");
                        var versionHeader = FindRequired<System.Windows.Controls.TextBlock>(inspector, "SnoopVersionHeader");
                        var version = coreAssembly.GetName().Version;
                        Require(inspectSnoop.Header as string == "检查 Snoop", "SnoopUI did not localize the self-inspection command");
                        Require(versionHeader.Text == $"版本: {version}", "SnoopUI did not localize the version header");
                        Require(fileMenu.Header as string == "文件", "SnoopUI did not load the zh-CN File menu resource");
                        Require(treeHeader.Content as string == "可视化树：", "SnoopUI did not load the zh-CN tree header");
                        Require(Thread.CurrentThread.CurrentUICulture.Name == "de-DE",
                            "inspector localization modified the inspected thread CurrentUICulture");

                        InspectorLocalization.Default.SetLanguage("en-US");
                        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                            System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
                        Require(fileMenu.Header as string == "File", "SnoopUI File menu did not live-switch to en-US");
                        Require(treeHeader.Content as string == "Tree:", "SnoopUI tree header did not live-switch to en-US");
                        Require(inspectSnoop.Header as string == "Snoop Snoop", "self-inspection command did not live-switch to en-US");
                        Require(versionHeader.Text == $"Version: {version}", "version header did not live-switch to en-US");

                        InspectorLocalization.Default.SetLanguage("system");
                        var expectedSystemFile = CultureInfo.InstalledUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                            ? "文件"
                            : "File";
                        Require(fileMenu.Header as string == expectedSystemFile,
                            "system language did not resolve independently of target thread culture");
                        Require(Thread.CurrentThread.CurrentUICulture.Name == "de-DE",
                            "system language resolution changed the inspected thread CurrentUICulture");

                        InspectorFilterSmoke.Verify(inspector);
                        InspectorDispatcherSmoke.Verify(app, inspector, fileMenu);
                        InspectorThemeResourceSmoke.Verify(inspector);
                        InspectorDensitySmoke.Verify(app, inspector, Path.GetDirectoryName(file)!);
                        WorkbenchLayoutSmoke.Verify(inspector, Path.GetDirectoryName(file)!);
                        WorkbenchPaneSmoke.Verify(inspector, Path.GetDirectoryName(file)!);
                        WorkbenchLayoutPresetSmoke.Verify(inspector, Path.GetDirectoryName(file)!);
                        WorkbenchNavigationSmoke.Verify(inspector);
                        InspectorAuxiliaryViewSmoke.Verify();
                        InspectorPropertyAndDebugSmoke.Verify();
                        InspectorRemainingViewSmoke.Verify();
                    }
                    finally
                    {
                        inspector.Close();
                        app.Shutdown();
                    }

                    Thread.CurrentThread.CurrentUICulture = inspectorCulture;
                    Console.WriteLine("PASS: SnoopUI embedded EN/ZH resources, live bindings and target thread culture isolation");
                }
                finally
                {
                    Thread.CurrentThread.CurrentUICulture = inspectorCulture;
                    File.Delete(inspectorSettingsPath);
                }

                Settings.Default.SettingsFile = persistedSettingsPath;
                Settings.Default.LanguageTag = persistedLanguage;
                LocalizationManager.Default.SetLanguage(originalManagerLanguage);
            }
            finally
            {
                Settings.Default.SettingsFile = persistedSettingsPath;
                Settings.Default.LanguageTag = persistedLanguage;
                LocalizationManager.Default.SetLanguage(originalManagerLanguage);
            }

            Console.WriteLine("PASS: CLR4 Snoop host-only XML language preference, reload, reset, legacy fallback and culture isolation");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static T FindRequired<T>(System.Windows.FrameworkElement element, string name)
        where T : class
    {
        var result = element.FindName(name) as T;
        if (result == null)
        {
            throw new InvalidOperationException("Required SettingsView control was not found: " + name);
        }

        return result;
    }

    private static void Require(bool result, string description)
    {
        if (!result)
        {
            throw new InvalidOperationException(description);
        }
    }
}
