// (c) Copyright Cory Plotts.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

namespace Snoop.Controls.ValueEditors;

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Snoop.Converters;
using Snoop.Infrastructure;

/// <summary>
///     Interaction logic for MouseWheelValueEditor.xaml
/// </summary>
public partial class MouseWheelValueEditor
{
    public MouseWheelValueEditor()
    {
        this.InitializeComponent();

        this.MouseWheel += this.MouseWheelHandler;
    }

    private PropertyInformation? PropertyInfo => this.DataContext as PropertyInformation;

    private void MouseWheelHandler(object sender, MouseWheelEventArgs e)
    {
        var fe = e.OriginalSource as FrameworkElement;
        if (fe is null)
        {
            return;
        }

        var increment = true;
        var largeIncrement = false;
        var tinyIncrement = false;

        if (e.Delta > 0)
        {
            increment = false;
        }

        if ((Keyboard.GetKeyStates(Key.LeftShift) & KeyStates.Down) > 0
            || (Keyboard.GetKeyStates(Key.RightShift) & KeyStates.Down) > 0)
        {
            largeIncrement = true;
        }

        if ((Keyboard.GetKeyStates(Key.LeftCtrl) & KeyStates.Down) > 0
            || (Keyboard.GetKeyStates(Key.RightCtrl) & KeyStates.Down) > 0)
        {
            tinyIncrement = true;
        }

        if (fe is TextBlock { Tag: not null } field && this.PropertyInfo is { CanEdit: true, Property: not null } property)
        {
            var fieldNum = int.Parse(field.Tag.ToString()!);
            var nextValue = property.PropertyType.Name switch
            {
                "Double" => ChangeDoubleValue(field.Text, increment, largeIncrement, tinyIncrement),
                "Int32" or "Int16" => ChangeIntValue(field.Text, increment, largeIncrement),
                "Boolean" => ChangeBooleanValue(field.Text),
                "Visibility" => ChangeEnumValue<Visibility>(field.Text, increment),
                "HorizontalAlignment" => ChangeEnumValue<HorizontalAlignment>(field.Text, increment),
                "VerticalAlignment" => ChangeEnumValue<VerticalAlignment>(field.Text, increment),
                "Thickness" => ChangeThicknessValuePart(property.StringValue, fieldNum, field.Text, increment, largeIncrement),
                "Brush" or "Color" => ChangeBrushValuePart(property.StringValue, fieldNum, field.Text, increment, largeIncrement),
                _ => null
            };

            if (nextValue is not null && nextValue != property.StringValue
                && StringValueConverter.TryConvertFromString(property.PropertyType.Type, nextValue, out var converted))
            {
                ValueEditor.CommitUserValue(property, converted);
            }
        }

        e.Handled = true;
    }

    private static string ChangeIntValue(string current, bool increase, bool largeIncrement)
    {
        var change = 1;
        if (!increase)
        {
            change *= -1;
        }

        if (largeIncrement)
        {
            change *= 10;
        }

        var ret = int.Parse(current);
        ret += change;

        return ret.ToString();
    }

    private static string ChangeDoubleValue(string current, bool increase, bool largeIncrement, bool tinyIncrement)
    {
        double change = 1;
        if (!increase)
        {
            change *= -1;
        }

        if (largeIncrement)
        {
            change *= 10;
        }

        if (tinyIncrement)
        {
            change /= 10;
        }

        var ret = double.Parse(current);
        ret += change;

        return ret.ToString();
    }

    private static string ChangeBooleanValue(string current)
    {
        var ret = bool.Parse(current);
        ret = !ret;

        return ret.ToString();
    }

    private static string ChangeEnumValue<T>(string current, bool increase)
    {
        var ret = (T)Enum.Parse(typeof(T), current);

        // make numeric, so we can add or subtract one
        var value = Convert.ToInt32(ret);
        if (increase)
        {
            value += 1;
        }
        else
        {
            value -= 1;
        }

        value = Math.Min(Enum.GetValues(typeof(T)).Length - 1, Math.Max(0, value));
        // long way around to get the enum typed value from the integer
        ret = (T)Enum.GetValues(typeof(T)).GetValue(value)!;

        return ret.ToString()!;
    }

    /// <summary>
    ///     Increments or decrements the field part in the Thickness value.
    ///     Replaces that field in the underlying VALUE
    /// </summary>
    private static string ChangeThicknessValuePart(string currentValue, int fieldNum, string current, bool increase, bool largeIncrement)
    {
        var change = 1;
        if (!increase)
        {
            change *= -1;
        }

        if (largeIncrement)
        {
            change *= 20;
        }

        var newVal = int.Parse(current);
        newVal += change;

        var partValue = newVal.ToString();

        // chop the current value up into its parts
        var fields = currentValue.Split(',');

        // replace the appropriate field
        fields[fieldNum - 1] = partValue;

        // re-assemble back to Brush value
        return string.Format(@"{0},{1},{2},{3}", fields[0], fields[1], fields[2], fields[3]);
    }

    /// <summary>
    ///     Increments or decrements the field part in the Brush value.
    ///     Replaces that field in the underlying VALUE
    /// </summary>
    private static string ChangeBrushValuePart(string currentValue, int fieldNum, string current, bool increase, bool largeIncrement)
    {
        var change = 1;
        if (!increase)
        {
            change *= -1;
        }

        if (largeIncrement)
        {
            change *= 16;
        }

        var ret = int.Parse(current, NumberStyles.HexNumber);
        ret = Math.Min(255, Math.Max(0, ret + change));

        var partValue = ret.ToString("X2");

        // chop the current value up into its parts
        var fields = new string[4];
        fields[0] = currentValue.Substring(1, 2); // start at 1 to skip the leading # sign
        fields[1] = currentValue.Substring(3, 2);
        fields[2] = currentValue.Substring(5, 2);
        fields[3] = currentValue.Substring(7, 2);

        // replace the appropriate field
        fields[fieldNum - 1] = partValue;

        // re-assemble back to Brush value
        return string.Format(@"#{0}{1}{2}{3}", fields[0], fields[1], fields[2], fields[3]);
    }
}