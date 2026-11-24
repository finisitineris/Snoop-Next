namespace Snoop.Infrastructure.Diagnostics.Providers;

using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;
using Snoop.Data.Tree;

public class BindingDiagnosticProvider : DiagnosticProvider
{
    public override string Name => "Local binding failures";

    public override string Description => "Reports path or update errors on locally bound dependency properties.";

    protected override IEnumerable<DiagnosticItem> GetDiagnosticItemsInternal(TreeItem treeItem)
    {
        if (treeItem.Target is not DependencyObject target || !target.CheckAccess())
        {
            yield break;
        }

        var localValues = target.GetLocalValueEnumerator();
        while (localValues.MoveNext())
        {
            var property = localValues.Current.Property;
            var expression = BindingOperations.GetBindingExpressionBase(target, property);
            if (expression is null
                || expression.Status is not (BindingStatus.PathError or BindingStatus.UpdateTargetError or BindingStatus.UpdateSourceError)
                || DependencyPropertyHelper.GetValueSource(target, property).BaseValueSource != BaseValueSource.Local)
            {
                continue;
            }

            var status = expression.Status;
            var propertyName = $"{property.OwnerType.Name}.{property.Name}";
            var elementName = treeItem.DisplayName;
            var path = (expression as BindingExpression)?.ParentBinding.Path?.Path;
            var hasPath = !string.IsNullOrEmpty(path);
            var description = hasPath
                ? $"Binding path '{path}' for '{propertyName}' on '{elementName}' failed with {status}."
                : $"The binding for '{propertyName}' on '{elementName}' failed with {status}.";

            yield return new(this, "Binding error", description, DiagnosticArea.Binding, DiagnosticLevel.Error)
            {
                DisplayNameResourceKey = "Diagnostics.Result.BindingError",
                DisplayDescriptionResourceKey = hasPath
                    ? "Diagnostics.Result.BindingFailureWithPath"
                    : "Diagnostics.Result.BindingFailure",
                DisplayDescriptionArguments = hasPath
                    ? new object[] { propertyName, elementName, status, path! }
                    : new object[] { propertyName, elementName, status },
                SourceObject = target,
                TargetProperty = property,
                TreeItem = treeItem,
                Dispatcher = target.Dispatcher,
            };
        }
    }
}
