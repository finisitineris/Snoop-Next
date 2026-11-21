namespace Snoop.Next.Localization;

using System;
using System.Globalization;

/// <summary>Canonical persisted preference values, independent of OS culture.</summary>
public static class LanguageTagPolicy
{
    public const string DefaultLanguageTag = "zh-CN";

    /// <summary>Maps a UI culture to the supported text resource language.</summary>
    public static string GetResourceLanguageTag(CultureInfo uiCulture)
    {
        return uiCulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en-US";
    }

    /// <summary>Malformed or missing legacy XML values safely use the default.</summary>
    public static string Normalize(string? languageTag)
    {
        if (string.Equals(languageTag, "en-US", StringComparison.OrdinalIgnoreCase))
        {
            return "en-US";
        }

        if (string.Equals(languageTag, "zh-CN", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-CN";
        }

        if (string.Equals(languageTag, "system", StringComparison.OrdinalIgnoreCase))
        {
            return "system";
        }

        return DefaultLanguageTag;
    }
}
