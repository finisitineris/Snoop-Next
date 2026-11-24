namespace Snoop.Localization;

using System;
using System.Globalization;
using System.Windows.Data;
using Snoop.Infrastructure.Diagnostics;
using Snoop.Infrastructure.Diagnostics.Providers;

/// <summary>Localizes diagnostic controls without changing diagnostic or export data.</summary>
public sealed class DiagnosticLabelConverter : IMultiValueConverter
{
    public static DiagnosticLabelConverter Default { get; } = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // The second binding refreshes this caption when the inspector language changes.
        var value = values[0];
        if (value is DiagnosticItem item)
        {
            return Equals(parameter, nameof(DiagnosticItem.Description))
                ? GetDescription(item)
                : GetName(item);
        }

        var key = value switch
        {
            DiagnosticProvider provider => GetProviderKey(provider),
            DiagnosticLevel.Info => "Diagnostics.Level.Info",
            DiagnosticLevel.Warning => "Diagnostics.Level.Warning",
            DiagnosticLevel.Error => "Diagnostics.Level.Error",
            DiagnosticLevel.Critical => "Diagnostics.Level.Critical",
            DiagnosticArea.Binding => "Diagnostics.Area.Binding",
            DiagnosticArea.Resource => "Diagnostics.Area.Resource",
            DiagnosticArea.Performance => "Diagnostics.Area.Performance",
            DiagnosticArea.Maintainability => "Diagnostics.Area.Maintainability",
            DiagnosticArea.Misc => "Diagnostics.Area.Misc",
            DiagnosticArea.Highlight => "Diagnostics.Area.Highlight",
            _ => null,
        };

        return key is null
            ? (value as DiagnosticProvider)?.Name ?? (value as Enum)?.ToString() ?? string.Empty
            : InspectorLocalization.Default[key];
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static string GetName(DiagnosticItem item)
    {
        var key = item.DisplayNameResourceKey ?? (item.UsesProviderName ? GetProviderKey(item.DiagnosticProvider) : null);
        return key is null ? item.Name : InspectorLocalization.Default[key];
    }

    private static string GetDescription(DiagnosticItem item)
    {
        var localization = InspectorLocalization.Default;
        if (item.DisplayDescriptionResourceKey is { } key)
        {
            return string.Format(CultureInfo.InvariantCulture, localization[key],
                item.DisplayDescriptionArguments ?? Array.Empty<object>());
        }

        var providerKey = item.UsesProviderDescription ? GetProviderKey(item.DiagnosticProvider) : null;
        if (providerKey is null)
        {
            return item.Description;
        }

        var description = localization[providerKey + ".Description"];
        return item.DiagnosticProvider is NonVirtualizedListsDiagnosticProvider
            ? string.Format(CultureInfo.InvariantCulture, description, NonVirtualizedListsDiagnosticProvider.Threshold)
            : description;
    }

    private static string? GetProviderKey(DiagnosticProvider provider)
    {
        return provider switch
        {
            BindingDiagnosticProvider => "Diagnostics.Provider.Binding",
            BindingLeakDiagnosticProvider => "Diagnostics.Provider.BindingLeak",
            FreezeFreezablesDiagnosticProvider => "Diagnostics.Provider.FreezeFreezables",
            LocalResourceDefinitionsDiagnosticProvider => "Diagnostics.Provider.LocalResources",
            MissingAdornerLayerDiagnosticProvider => "Diagnostics.Provider.MissingAdorner",
            NonVirtualizedListsDiagnosticProvider => "Diagnostics.Provider.NonVirtualizedLists",
            UnresolvedDynamicResourceDiagnosticProvider => "Diagnostics.Provider.UnresolvedDynamicResource",
            _ => null,
        };
    }
}
