// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop.Controls.ValueEditors;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using JetBrains.Annotations;

public class EnumValueEditor : ValueEditor, INotifyPropertyChanged
{
    public ObservableCollection<EnumValueWrapper> Values { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public EnumValueWrapper? SelectedChoice
    {
        get => this.Values.FirstOrDefault(choice => Equals(choice.Value, this.Value));
        set
        {
            // A recycled ComboBox temporarily has no selection. The nullable
            // enum choice is a real wrapper whose Value is null.
            if (value is not null && this.Values.Contains(value))
            {
                CommitUserValue(this.PropertyInfo, value.Value);
            }
        }
    }

    protected override void OnPropertyInfoChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyInfoChanged(e);

        this.Values.Clear();

        var enumType = this.PropertyInfo?.PropertyType.Type;

        if (enumType is not null && Nullable.GetUnderlyingType(enumType) is { IsEnum: true } underlyingType)
        {
            enumType = underlyingType;
            this.Values.Add(new EnumValueWrapper(null, "<NULL>"));
        }

        if (enumType?.IsEnum == true)
        {
            var values = Enum.GetValues(enumType);
            var names = Enum.GetNames(enumType);

            for (var i = 0; i < values.Length; i++)
            {
                var value = values.GetValue(i);
                this.Values.Add(new EnumValueWrapper(value, names[i]));
            }
        }

        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.SelectedChoice)));
    }

    protected override void OnValueChanged(object? newValue)
    {
        base.OnValueChanged(newValue);

        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.SelectedChoice)));
    }
}

[PublicAPI]
public class EnumValueWrapper : INotifyPropertyChanged
{
#pragma warning disable CS0067
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

    public EnumValueWrapper(object? value, string text)
    {
        this.Value = value;
        this.Text = text;
    }

    public object? Value { get; }

    public string Text { get; }
}