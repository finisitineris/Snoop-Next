// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop.Controls.ValueEditors;

using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Snoop.Converters;
using Snoop.Infrastructure;

public class StandardValueEditor : ValueEditor
{
    public static readonly DependencyProperty StringValueProperty =
        DependencyProperty.Register(
            nameof(StringValue),
            typeof(string),
            typeof(StandardValueEditor),
            new PropertyMetadata(OnStringValueChanged));

    private bool isUpdatingValue;

    public string? StringValue
    {
        get => (string?)this.GetValue(StringValueProperty);
        set => this.SetValue(StringValueProperty, value);
    }

    private static void OnStringValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        ((StandardValueEditor)sender).OnStringValueChanged((string?)e.NewValue);
    }

    protected virtual void OnStringValueChanged(string? newValue)
    {
        if (this.isUpdatingValue || !this.IsEditable || this.PropertyInfo is not { Property: not null } property)
        {
            return;
        }

        if (!StringValueConverter.TryConvertFromString(property.PropertyType.Type, newValue, out var converted))
        {
            property.SetEditValidation(PropertyEditValidation.InvalidFormat, newValue);
            return;
        }

        if (!CommitUserValue(property, converted))
        {
            property.SetEditValidation(PropertyEditValidation.RejectedValue, newValue);
        }
    }

    protected override void OnValueChanged(object? newValue)
    {
        this.isUpdatingValue = true;
        this.StringValue = this.PropertyInfo?.RejectedEditText ?? StringValueConverter.ConvertToString(newValue) ?? string.Empty;
        this.isUpdatingValue = false;

        var binding = BindingOperations.GetBindingExpression(this, StringValueProperty);
        binding?.UpdateSource();
    }

    private void OnEditValidationChanged(object? sender, PropertyChangedEventArgs e)
    {
        this.OnValueChanged(this.Value);
    }

    protected override void OnPropertyInfoChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyInfoChanged(e);

        this.IsEditable = this.PropertyInfo?.CanEdit is true
                          && StringValueConverter.CanConvertFromString(this.PropertyInfo?.PropertyType.Type);

        if (e.OldValue is PropertyInformation oldProperty)
        {
            PropertyChangedEventManager.RemoveHandler(oldProperty, this.OnEditValidationChanged, nameof(PropertyInformation.EditValidation));
        }

        if (this.PropertyInfo is { } property)
        {
            PropertyChangedEventManager.AddHandler(property, this.OnEditValidationChanged, nameof(PropertyInformation.EditValidation));
            this.OnValueChanged(property.Value);
        }
    }
}
