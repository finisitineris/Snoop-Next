namespace Snoop;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;

public sealed class LauncherTargetPicker : ComboBox
{
    private DispatcherOperation? pendingPeerRefresh;

    protected override AutomationPeer OnCreateAutomationPeer() => new TargetPickerPeer(this);

    protected override void OnDropDownOpened(EventArgs e)
    {
        base.OnDropDownOpened(e);
        UIElementAutomationPeer.FromElement(this)?.ResetChildrenCache();
    }

    protected override void OnDropDownClosed(EventArgs e)
    {
        base.OnDropDownClosed(e);
        UIElementAutomationPeer.FromElement(this)?.ResetChildrenCache();
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        if (this.pendingPeerRefresh is null && UIElementAutomationPeer.FromElement(this) is { } peer)
        {
            // A filtered view resets the generated rows. Rebuild the cached UIA
            // children after layout, once per batch of collection changes.
            this.pendingPeerRefresh = this.Dispatcher.InvokeAsync(() =>
            {
                this.pendingPeerRefresh = null;
                peer.ResetChildrenCache();
            }, DispatcherPriority.Input);
        }
    }

    private sealed class TargetPickerPeer : ComboBoxAutomationPeer
    {
        public TargetPickerPeer(LauncherTargetPicker owner)
            : base(owner)
        {
        }

        protected override List<AutomationPeer> GetChildrenCore()
        {
            var picker = (LauncherTargetPicker)this.Owner;
            var items = base.GetChildrenCore();
            if (!picker.IsDropDownOpen)
            {
                return items;
            }

            // ComboBox exposes the item peers but skips extra controls in its popup template.
            var children = new List<AutomationPeer>();
            AddTemplateControl("TargetSearchBox");
            AddTemplateControl("ClearTargetSearchButton");
            if (items is not null)
            {
                children.AddRange(items);
            }

            return children;

            void AddTemplateControl(string name)
            {
                if (picker.Template.FindName(name, picker) is UIElement control
                    && UIElementAutomationPeer.CreatePeerForElement(control) is { } peer)
                {
                    children.Add(peer);
                }
            }
        }
    }
}
