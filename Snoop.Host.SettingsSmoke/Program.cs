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
