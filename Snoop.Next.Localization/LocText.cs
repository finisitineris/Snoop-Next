namespace Snoop.Next.Localization;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

/// <summary>
/// Style-compatible TextBlock localization. A WPF Setter cannot reliably
/// host a live Binding as Setter.Value: use a semantic key instead.
/// </summary>
public static class LocText
{
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached(
            "Key", typeof(string), typeof(LocText),
            new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject target)
    {
        return (string?)target.GetValue(KeyProperty);
    }

    public static void SetKey(DependencyObject target, string? key)
    {
        target.SetValue(KeyProperty, key);
    }

    private static void OnKeyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (!(target is TextBlock textBlock))
        {
            return;
        }

        var key = args.NewValue as string;
        if (!string.IsNullOrWhiteSpace(key))
        {
            BindingOperations.SetBinding(textBlock, TextBlock.TextProperty,
                new LocExtension(key).CreateBinding());
            return;
        }

        // Clear only the binding created by this behavior; never erase
        // an unrelated Text binding installed by the host.
        var oldKey = args.OldValue as string;
        var existing = BindingOperations.GetBinding(textBlock, TextBlock.TextProperty);
        if (existing != null && ReferenceEquals(existing.Source, LocalizationManager.Default)
            && existing.Path?.Path == "[" + oldKey + "]")
        {
            BindingOperations.ClearBinding(textBlock, TextBlock.TextProperty);
        }
    }
}
