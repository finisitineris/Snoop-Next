namespace Snoop.Host.SettingsSmoke;

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Snoop;

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

            // Instantiate the real host SettingsView on STA without showing a
            // window or attaching Snoop to any external process. This catches
            // runtime BAML, static resource, and selector wiring failures.
            var settingsView = new Snoop.Views.SettingsView();
            var languagePicker = settingsView.FindName("LanguageSelector") as System.Windows.Controls.ComboBox;
            if (languagePicker == null)
            {
                throw new InvalidOperationException("host SettingsView language selector was not created");
            }

            Require(languagePicker.Items.Count == 3, "expected zh-CN/en-US/system in host picker");
            Require(string.Equals(languagePicker.SelectedValue as string, Settings.Default.LanguageTag, StringComparison.Ordinal),
                "host picker selection does not match the Snoop host setting");
            Require(Thread.CurrentThread.CurrentUICulture.Equals(originalCulture),
                "constructing Snoop host SettingsView modified thread UI culture");
            Console.WriteLine("PASS: host SettingsView BAML loaded and language selector wired on STA");

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

    private static void Require(bool result, string description)
    {
        if (!result)
        {
            throw new InvalidOperationException(description);
        }
    }
}
