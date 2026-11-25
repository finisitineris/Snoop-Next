// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop.Controls.ValueEditors;

using System.ComponentModel;

public class BoolValueEditor : ValueEditor, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool? CheckedValue
    {
        get => this.Value as bool?;
        set => CommitUserValue(this.PropertyInfo, value);
    }

    protected override void OnValueChanged(object? newValue)
    {
        base.OnValueChanged(newValue);
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.CheckedValue)));
    }
}