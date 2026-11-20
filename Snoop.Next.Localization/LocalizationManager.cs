namespace Snoop.Next.Localization;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Resources;

/// <summary>
/// Snoop-owned UI text. Does NOT change Thread.CurrentUICulture, default
/// thread culture, Application.Resources, or the inspected WPF app.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources =
        new ResourceManager("Snoop.Next.Localization.Strings", typeof(LocalizationManager).Assembly);

    private readonly object missingKeysSync = new object();
    private readonly HashSet<string> reportedMissingKeys = new HashSet<string>(StringComparer.Ordinal);
    private CultureInfo uiCulture;

    public LocalizationManager()
        : this("zh-CN")
    {
    }

    public LocalizationManager(string languageTag)
    {
        this.LanguageTag = languageTag;
        this.uiCulture = ResolveCulture(languageTag);
    }

    /// <summary>Default service for lightweight {local:Loc Key} bindings.</summary>
    public static LocalizationManager Default { get; } = new LocalizationManager();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LanguageTag { get; private set; }

    public CultureInfo UICulture => this.uiCulture;

    /// <summary>Maps the host's effective UI culture to one of Snoop's supported resource languages.</summary>
    public string EffectiveLanguageTag =>
        this.uiCulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A localization key is required.", nameof(key));
            }

            var value = Resources.GetString(key, this.uiCulture)
                        ?? Resources.GetString(key, CultureInfo.GetCultureInfo("en-US"));
            if (value != null)
            {
                return value;
            }

            lock (this.missingKeysSync)
            {
                if (this.reportedMissingKeys.Add(key))
                {
                    Trace.WriteLine("Snoop Next missing RESX key: " + key);
                }
            }

            return "[Missing: " + key + "]";
        }
    }

    public string Format(string key, params object[] args)
    {
        return string.Format(this.uiCulture, this[key], args);
    }

    /// <summary>
    /// Call from the owning Snoop UI Dispatcher. WPF listens for the standard
    /// "Item[]" indexer notification and refreshes all key bindings.
    /// </summary>
    public void SetLanguage(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            throw new ArgumentException("Language is required.", nameof(languageTag));
        }

        var next = ResolveCulture(languageTag);
        if (this.LanguageTag == languageTag && this.uiCulture.Equals(next))
        {
            return;
        }

        this.LanguageTag = languageTag;
        this.uiCulture = next;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.LanguageTag)));
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.UICulture)));
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.EffectiveLanguageTag)));
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private static CultureInfo ResolveCulture(string tag)
    {
        switch (tag)
        {
            case "en-US":
                return CultureInfo.GetCultureInfo("en-US");
            case "zh-CN":
                return CultureInfo.GetCultureInfo("zh-CN");
            case "system":
                // Read only. Never change the host application's cultures.
                return CultureInfo.CurrentUICulture;
            default:
                throw new ArgumentOutOfRangeException(nameof(tag), tag, "Supported languages: en-US, zh-CN, system.");
        }
    }
}
