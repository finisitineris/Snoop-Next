namespace Snoop.Infrastructure.Helpers;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Snoop.Localization;

internal static class PropertyOriginFormatter
{
    public static string GetDetails(DependencyObject target, DependencyProperty property)
    {
        var lines = new List<string>();
        var expression = BindingOperations.GetBindingExpressionBase(target, property);
        if (expression is not null)
        {
            AddBindingDetails(lines, expression);
        }

        var source = DependencyPropertyHelper.GetValueSource(target, property);
        lines.Add(Format("PropertyOrigin.BaseSource", source.BaseValueSource));
        lines.Add(Format("PropertyOrigin.Modifiers", source.IsExpression, source.IsAnimated, source.IsCoerced));
        lines.Add(Format("PropertyOrigin.LocalValue", DescribeValue(() => target.ReadLocalValue(property))));
        lines.Add(Format("PropertyOrigin.EffectiveValue", DescribeValue(() => target.GetValue(property))));
        return string.Join(Environment.NewLine, lines);
    }

    internal static string GetBindingDetails(BindingExpressionBase expression)
    {
        var lines = new List<string>();
        AddBindingDetails(lines, expression);
        if (expression.ValidationError?.ErrorContent is string error && error.Length != 0)
        {
            lines.Add(error);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddBindingDetails(List<string> lines, BindingExpressionBase expression)
    {
        var bindingType = Format("PropertyOrigin.Binding", expression.ParentBindingBase.GetType().Name);
        var status = Format("PropertyOrigin.Status", expression.Status);
        if (expression is BindingExpression single)
        {
            AddPathAndSource(lines, single);
            lines.Add(status);
            lines.Add(bindingType);
        }
        else
        {
            lines.Add(bindingType);
            lines.Add(status);
            if (expression is MultiBindingExpression multi)
            {
                AddChildren(lines, multi.BindingExpressions, null);
            }
            else if (expression is PriorityBindingExpression priority)
            {
                AddChildren(lines, priority.BindingExpressions, priority.ActiveBindingExpression);
            }
        }
    }

    private static void AddPathAndSource(List<string> lines, BindingExpression expression)
    {
        var binding = expression.ParentBinding;
        var path = binding.Path?.Path;
        lines.Add(Format("PropertyOrigin.Path", path is { Length: > 0 } ? path : "."));
        if (!string.IsNullOrEmpty(binding.XPath))
        {
            lines.Add($"XPath: {binding.XPath}");
        }

        // DataItem comes from the existing expression. Never walk the path or
        // invoke ToString() on the inspected application's data context.
        var dataItem = expression.DataItem;
        var source = dataItem is null || ReferenceEquals(dataItem, DependencyProperty.UnsetValue)
            ? InspectorLocalization.Default["PropertyOrigin.Unavailable"]
            : ReferenceEquals(dataItem, BindingOperations.DisconnectedSource)
                ? "BindingOperations.DisconnectedSource"
                : dataItem.GetType().FullName ?? dataItem.GetType().Name;

        if (!string.IsNullOrEmpty(binding.ElementName))
        {
            source += $" (ElementName={binding.ElementName})";
        }
        else if (binding.RelativeSource is { } relativeSource)
        {
            source += $" (RelativeSource={relativeSource.Mode})";
        }

        lines.Add(Format("PropertyOrigin.Source", source));
    }

    private static void AddChildren(List<string> lines, ReadOnlyCollection<BindingExpressionBase> children, BindingExpressionBase? active)
    {
        // Keep a large MultiBinding readable. Always include the active
        // PriorityBinding child even if it falls outside the preview.
        var shown = Math.Min(3, children.Count);
        for (var index = 0; index < shown; index++)
        {
            AddChild(index);
        }

        var activeIndex = active is null ? -1 : children.IndexOf(active);
        if (activeIndex >= shown)
        {
            AddChild(activeIndex);
            shown++;
        }

        if (shown < children.Count)
        {
            lines.Add(Format("PropertyOrigin.RemainingChildren", children.Count - shown));
        }

        void AddChild(int index)
        {
            var child = children[index];
            lines.Add(ReferenceEquals(child, active)
                ? Format("PropertyOrigin.ActiveChild", index + 1)
                : Format("PropertyOrigin.Child", index + 1));
            lines.Add(Format("PropertyOrigin.Status", child.Status));
            if (child is BindingExpression bindingExpression)
            {
                AddPathAndSource(lines, bindingExpression);
            }
        }
    }

    private static string DescribeValue(Func<object?> readValue)
    {
        // Resolving a deferred WPF resource or reading an animated value can
        // run target code. A failed read should leave the other details usable.
        object? value;
        try
        {
            value = readValue();
        }
        catch (Exception)
        {
            return InspectorLocalization.Default["PropertyOrigin.Unavailable"];
        }

        if (value is null)
        {
            return "null";
        }

        if (ReferenceEquals(value, DependencyProperty.UnsetValue))
        {
            return "DependencyProperty.UnsetValue";
        }

        if (value is string text)
        {
            return text.Length <= 256 ? text : text.Substring(0, 256) + "…";
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is decimal)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return type.FullName ?? type.Name;
    }

    private static string Format(string resourceKey, params object[] arguments)
        => string.Format(CultureInfo.InvariantCulture, InspectorLocalization.Default[resourceKey], arguments);
}
