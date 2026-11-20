namespace Snoop.Host.SettingsSmoke;

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Snoop;
using Snoop.Next.Localization;

/// <summary>
/// Tests Snoop's host-only XML preference without launching any WPF inspector,
/// network endpoint, process attachment, or injection. The runner may have a
/// newer .NET Framework 4.x installed; this is not a 4.6.2-only runtime test.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var path = Path.Combine(Path.GetTempPath(), "SnoopHostSettings-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(path, "SnoopSettings.xml");
        var originalCulture = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Directory.CreateDirectory(path);
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
                Require(Settings.Default.LanguageTag == "zh-CN", "Reset did not reset in-memory setting");
                Require(LocalizationManager.Default.LanguageTag == "en-US", "Reset applied language before Save");
                Require(File.ReadAllText(file).Contains("<LanguageTag>en-US</LanguageTag>"), "Reset unexpectedly persisted before Save");

                var saveResetButton = FindRequired<System.Windows.Controls.Button>(resetView, "SaveSettingsAndClose");
                saveResetButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Require(LocalizationManager.Default.LanguageTag == "zh-CN", "Save after Reset did not apply default language");

                Console.WriteLine("PASS: SettingsView pending selection, Save, Discard and Reset behavior");

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
        finally
        {
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
