namespace Snoop.Infrastructure;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

internal sealed class PropertyEditHistory
{
    private const int UndoLimit = 100;
    private readonly List<Entry> past = new();
    private readonly List<Entry> future = new();
    private bool replaying;

    internal bool IsClosed { get; private set; }

    internal void EndSession()
    {
        this.IsClosed = true;
        this.past.Clear();
        this.future.Clear();
        CommandManager.InvalidateRequerySuggested();
    }

    // Rows can also wrap a setter, converter or an unrelated binding. Only the
    // grid's direct, two-way DP binding describes the property we could restore.
    internal static object? CaptureLiteral(PropertyInformation row, BindingExpressionBase binding)
    {
        if (row.Target is DependencyObject target && row.DependencyProperty is { } dp
            && binding is BindingExpression { ParentBinding: { Mode: BindingMode.TwoWay, Converter: null } pathBinding }
            && ReferenceEquals(pathBinding.Source, target) && pathBinding.Path is { Path: "(0)" } path
            && path.PathParameters.Count == 1 && ReferenceEquals(path.PathParameters[0], dp))
        {
            return ReadLocalLiteral(target, dp);
        }

        return DependencyProperty.UnsetValue;
    }

    private static object? ReadLocalLiteral(DependencyObject target, DependencyProperty dp)
    {
        if (!target.CheckAccess() || target.IsSealed || dp.ReadOnly)
        {
            return DependencyProperty.UnsetValue;
        }

        var source = DependencyPropertyHelper.GetValueSource(target, dp);
        if (source.BaseValueSource != BaseValueSource.Local || source.IsExpression || source.IsAnimated
            || source.IsCoerced || source.IsCurrent || BindingOperations.IsDataBound(target, dp))
        {
            return DependencyProperty.UnsetValue;
        }

        var value = target.ReadLocalValue(dp);
        // An object-typed DP such as Button.Content can hold a literal string
        // or a whole visual tree. Test the value itself at both ends of an edit.
        return value is null or string or bool or char or byte or sbyte or short or ushort
            or int or uint or long or ulong or float or double or decimal or Enum
            or DateTime or DateTimeOffset or TimeSpan or Guid ? value : DependencyProperty.UnsetValue;
    }

    internal void Record(PropertyInformation row, object? before, object? after, bool previouslyEdited)
    {
        if (this.IsClosed)
        {
            return;
        }

        this.future.Clear();
        if (row.Target is DependencyObject target && row.DependencyProperty is { } dp)
        {
            // A new edit can encounter outside changes to this same DP before a
            // command requery has had a chance to notice the divergent history.
            if (this.past.FindLast(entry => entry.Matches(target, dp)) is { } previous && !Equals(previous.After, before))
            {
                this.Forget(target, dp);
            }

            if (before != DependencyProperty.UnsetValue && after != DependencyProperty.UnsetValue && !Equals(before, after))
            {
                if (this.past.Count == UndoLimit)
                {
                    this.past.RemoveAt(0);
                }

                this.past.Add(new Entry(target, dp, before, after, previouslyEdited));
            }
            else
            {
                this.Forget(target, dp);
            }
        }

        CommandManager.InvalidateRequerySuggested();
    }

    internal void ObservedValueChanged(PropertyInformation row)
    {
        if (row.Target is DependencyObject target && row.DependencyProperty is { } dp
            && (Last(this.past)?.Matches(target, dp) == true || Last(this.future)?.Matches(target, dp) == true))
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }

    internal bool CanReplay(bool undo) => !this.IsClosed && !this.replaying && this.FindNext(undo, out _) is not null;

    internal bool Replay(bool undo, Action<DependencyObject, DependencyProperty, object?, bool> applied)
    {
        if (this.IsClosed || this.replaying || this.FindNext(undo, out var target) is not { } entry || target is null)
        {
            return false;
        }

        var from = undo ? this.past : this.future;
        var to = undo ? this.future : this.past;
        var value = undo ? entry.Before : entry.After;
        this.replaying = true;
        from.RemoveAt(from.Count - 1);
        try
        {
            // DP callbacks belong to the inspected application. It can still
            // reject this write, normalize the value, or install an expression.
            try
            {
                target.SetValue(entry.Property, value);
            }
            catch (Exception exception)
            {
                LogHelper.WriteWarning(exception);
                this.Forget(target, entry.Property);
                return false;
            }

            if (this.IsClosed || !Equals(ReadLocalLiteral(target, entry.Property), value))
            {
                this.Forget(target, entry.Property);
                return false;
            }

            to.Add(entry);
            applied(target, entry.Property, value, !undo || entry.PreviouslyEdited);
            return true;
        }
        finally
        {
            this.replaying = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private Entry? FindNext(bool undo, out DependencyObject? target)
    {
        var entries = undo ? this.past : this.future;
        while (Last(entries) is { } entry)
        {
            if (entry.Target.TryGetTarget(out target))
            {
                if (Equals(ReadLocalLiteral(target, entry.Property), undo ? entry.After : entry.Before))
                {
                    return entry;
                }

                // The application now owns this change. Older commands for the
                // same property could coincidentally match the external value.
                this.Forget(target, entry.Property);
            }
            else
            {
                entries.RemoveAt(entries.Count - 1);
            }

            // A button may first discover stale history in its click-time
            // CanExecute check, outside WPF's normal command requery.
            CommandManager.InvalidateRequerySuggested();
        }

        target = null;
        return null;
    }

    private void Forget(DependencyObject target, DependencyProperty dp)
    {
        this.past.RemoveAll(old => old.Matches(target, dp));
        this.future.RemoveAll(old => old.Matches(target, dp));
    }

    private static Entry? Last(List<Entry> entries) => entries.Count > 0 ? entries[entries.Count - 1] : null;

    private sealed class Entry(DependencyObject original, DependencyProperty property, object? before, object? after, bool previouslyEdited)
    {
        internal WeakReference<DependencyObject> Target { get; } = new(original);

        internal DependencyProperty Property { get; } = property;

        internal object? Before { get; } = before;

        internal object? After { get; } = after;

        internal bool PreviouslyEdited { get; } = previouslyEdited;

        internal bool Matches(DependencyObject target, DependencyProperty property)
            => this.Property == property && this.Target.TryGetTarget(out var original) && ReferenceEquals(original, target);
    }
}
