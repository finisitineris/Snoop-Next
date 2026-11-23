namespace Snoop.Infrastructure.Diagnostics.Providers;

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Snoop.Data.Tree;

public class FreezeFreezablesDiagnosticProvider : DiagnosticProvider
{
    public override string Name => "Freeze freezables";

    public override string Description => "You should freeze freezable to save memory and increase performance.";

    protected override IEnumerable<DiagnosticItem> GetDiagnosticItemsInternal(TreeItem treeItem)
    {
        if (treeItem.Target is not FrameworkElement frameworkElement)
        {
            return Enumerable.Empty<DiagnosticItem>();
        }

        return this.AnalyzeResourcesRecursive(frameworkElement.Resources, treeItem);
    }

    private IEnumerable<DiagnosticItem> AnalyzeResourcesRecursive(ResourceDictionary dictionary, TreeItem treeItem)
    {
        foreach (var resourceDictionary in dictionary.MergedDictionaries)
        {
            foreach (var item in this.AnalyzeResourcesRecursive(resourceDictionary, treeItem))
            {
                yield return item;
            }
        }

        foreach (var item in this.CheckResources(dictionary, treeItem))
        {
            yield return item;
        }
    }

    private IEnumerable<DiagnosticItem> CheckResources(ResourceDictionary dictionary, TreeItem treeItem)
    {
        foreach (var resourceKey in dictionary.Keys)
        {
            if (resourceKey is null)
            {
                continue;
            }

            if (dictionary.TryGetValue(resourceKey, out var resource)
                && resource is Freezable { IsFrozen: false } freezable)
            {
                var resourceKeyText = $"{resourceKey}";

                yield return
                    new(this,
                        "Freeze freezables",
                        $"Freezing the resource '{resourceKeyText}' can save memory and increase performance.",
                        DiagnosticArea.Performance,
                        DiagnosticLevel.Info)
                    {
                        DisplayNameResourceKey = "Diagnostics.Provider.FreezeFreezables",
                        DisplayDescriptionResourceKey = "Diagnostics.Result.FreezeResourceDescription",
                        DisplayDescriptionArguments = new object[] { resourceKeyText },
                        TreeItem = treeItem,
                        Dispatcher = freezable.Dispatcher,
                        SourceObject = dictionary
                    };
            }
        }
    }
}
