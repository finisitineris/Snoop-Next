namespace Snoop.Host.SettingsSmoke;

using System;
using System.IO;
using System.Windows;
using System.Xml.Serialization;
using Snoop.Core;
using Snoop.Data;
using Snoop.Infrastructure;
using Snoop.Localization;

internal static class AppDomainLanguageSmoke
{
    private const string SettingsFileVariable = "Snoop.SettingsFile";
    private const string SettingsRootKey = "Snoop.SettingsSmoke.AppDomainRoot";

    public static void Verify(string settingsRoot)
    {
        var originalSettingsFile = Environment.GetEnvironmentVariable(SettingsFileVariable);
        var originalInstallPath = Environment.GetEnvironmentVariable(SettingsHelper.SNOOP_INSTALL_PATH_ENV_VAR);
        var domain = AppDomain.CreateDomain("Snoop language reattach smoke", null, AppDomain.CurrentDomain.SetupInformation);
        try
        {
            domain.SetData(SettingsRootKey, settingsRoot);
            domain.DoCallBack(VerifyReattach);
        }
        finally
        {
            try
            {
                AppDomain.Unload(domain);
            }
            finally
            {
                Environment.SetEnvironmentVariable(SettingsFileVariable, originalSettingsFile);
                Environment.SetEnvironmentVariable(SettingsHelper.SNOOP_INSTALL_PATH_ENV_VAR, originalInstallPath);
            }
        }

        Console.WriteLine("PASS: secondary AppDomain reattach refreshes workbench language");
    }

    private static void VerifyReattach()
    {
        // With no presentation source, the actual callback initializes localization without creating an inspector window.
        foreach (var source in PresentationSource.CurrentSources)
        {
            throw new InvalidOperationException("The secondary AppDomain smoke test requires a windowless AppDomain.");
        }

        var settingsRoot = (string?)AppDomain.CurrentDomain.GetData(SettingsRootKey)
            ?? throw new InvalidOperationException("The secondary AppDomain smoke test is missing its temporary settings root.");
        var serializer = new XmlSerializer(typeof(TransientSettingsData));
        var firstSettingsFile = Path.Combine(settingsRoot, "FirstAttachment.xml");
        var nextSettingsFile = Path.Combine(settingsRoot, "NextAttachment.xml");

        AttachAndVerify(serializer, firstSettingsFile, "en-US", "File");
        File.Delete(firstSettingsFile);
        AttachAndVerify(serializer, nextSettingsFile, "zh-CN", "文件");
    }

    private static void AttachAndVerify(XmlSerializer serializer, string settingsFile, string language, string fileMenuText)
    {
        using (var stream = File.Create(settingsFile))
        {
            serializer.Serialize(stream, new TransientSettingsData { LanguageTag = language });
        }

        Environment.SetEnvironmentVariable(SettingsFileVariable, settingsFile);
        _ = new SnoopCrossAppDomainInjector();

        var loadedLanguage = TransientSettingsData.Current?.LanguageTag;
        var actualFileMenuText = InspectorLocalization.Default["Menu.File"];
        if (loadedLanguage != language || actualFileMenuText != fileMenuText)
        {
            throw new InvalidOperationException(
                $"Secondary AppDomain expected language '{language}' and File menu '{fileMenuText}', but loaded '{loadedLanguage}' and '{actualFileMenuText}'.");
        }
    }
}
