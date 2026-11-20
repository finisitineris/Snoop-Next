namespace Snoop.Localization;

using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Snoop.Data;

/// <summary>
/// Snoop inspector localization. Both languages are embedded in one neutral
/// Snoop.Core resource, avoiding a satellite or new runtime code assembly.
/// </summary>
public sealed class InspectorLocalization : INotifyPropertyChanged
{
    private static readonly ResourceManager resources =
        new ResourceManager("Snoop.Localization.InspectorStrings", typeof(InspectorLocalization).Assembly);

    private string languageTag = "system";

    public static InspectorLocalization Default { get; } = new InspectorLocalization();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LanguageTag => this.languageTag;

    public string EffectiveLanguageTag => this.ResolveEffectiveLanguageTag();

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A localization resource key is required.", nameof(key));
            }

            var language = this.ResolveEffectiveLanguageTag();
            return resources.GetString(language + "." + key, CultureInfo.InvariantCulture)
                   ?? resources.GetString("en-US." + key, CultureInfo.InvariantCulture)
                   ?? "[Missing: " + key + "]";
        }
    }

    public void InitializeFrom(TransientSettingsData settingsData)
    {
        if (settingsData == null)
        {
            throw new ArgumentNullException(nameof(settingsData));
        }

        this.SetLanguage(settingsData.LanguageTag);
    }

    public void SetLanguage(string? languageTag)
    {
        var next = Normalize(languageTag);
        if (string.Equals(next, this.languageTag, StringComparison.Ordinal))
        {
            return;
        }

        this.languageTag = next;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.LanguageTag)));
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.EffectiveLanguageTag)));
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private string ResolveEffectiveLanguageTag()
    {
        if (this.languageTag != "system")
        {
            return this.languageTag;
        }

        // Never inherit an inspected application's thread-specific CurrentUICulture.
        return CultureInfo.InstalledUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : "en-US";
    }

    private static string Normalize(string? languageTag)
    {
        if (string.Equals(languageTag, "zh-CN", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-CN";
        }

        if (string.Equals(languageTag, "en-US", StringComparison.OrdinalIgnoreCase))
        {
            return "en-US";
        }

        return "system";
    }
}
