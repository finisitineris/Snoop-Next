namespace Snoop.Data.Tree;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

internal static class TreeModeSelection
{
    internal static object GetInspectedRoot(TreeItem root) => GetPeerOwner(root.Target) ?? root.Target;

    internal static TreeItem Restore(TreeItem previous, TreeItem root, SystemResourcesTreeItem resources)
    {
        // Rank identities by distance from the old selection. Search only rows of the new
        // model, in one pass, without picking, reloading, or crossing the inspected scope.
        var candidates = new Dictionary<object, int>(ObjectIdentityComparer.Instance);
        var distance = 0;
        for (TreeItem? item = previous; item is not null; item = item.Parent, distance++)
        {
            var rank = 4 * distance;
            AddCandidate(item.Target, rank);
            AddCandidate(GetRepresentedObject(item.Target), rank + 2);
            if (item is SystemResourcesTreeItem)
            {
                root = resources;
                if (item != previous)
                {
                    root.IsExpanded = true;
                }
            }
        }

        var best = root;
        var bestRank = int.MaxValue;
        var remaining = new Stack<TreeItem>();
        remaining.Push(root);
        while (remaining.Count != 0)
        {
            var item = remaining.Pop();
            Consider(item, item.Target, 0);
            if (bestRank == 0)
            {
                break;
            }

            // Prefer the real element row to a peer representing it. This keeps the
            // property inspector on the same object when Automation exposes both.
            Consider(item, GetRepresentedObject(item.Target), 1);
            for (var index = item.Children.Count - 1; index >= 0; --index)
            {
                remaining.Push(item.Children[index]);
            }
        }

        return best;

        void AddCandidate(object? target, int rank)
        {
            if (target is not null && !candidates.ContainsKey(target))
            {
                candidates.Add(target, rank);
            }
        }

        void Consider(TreeItem item, object? target, int representationPenalty)
        {
            if (target is not null && candidates.TryGetValue(target, out var candidateRank))
            {
                var rank = candidateRank + representationPenalty;
                if (rank < bestRank)
                {
                    best = item;
                    bestRank = rank;
                }
            }
        }
    }

    private static object? GetPeerOwner(object target) => target switch
    {
        UIElementAutomationPeer peer => peer.Owner,
        UIElement3DAutomationPeer peer => peer.Owner,
        ContentElementAutomationPeer peer => peer.Owner,
        ItemAutomationPeer peer => peer.Item,
        _ => null
    };

    private static object? GetRepresentedObject(object target) => target switch
    {
        ResourceDictionaryWrapper dictionary => dictionary.ResourceDictionary,
        UIElement container when ItemsControl.ItemsControlFromItemContainer(container) is { } items
            => items.ItemContainerGenerator.ItemFromContainer(container) is { } item && item != DependencyProperty.UnsetValue ? item : null,
        _ => GetPeerOwner(target)
    };

    private sealed class ObjectIdentityComparer : IEqualityComparer<object>
    {
        internal static readonly ObjectIdentityComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }
}
