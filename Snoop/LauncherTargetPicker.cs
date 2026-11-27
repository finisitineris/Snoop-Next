namespace Snoop;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

public sealed class LauncherTargetPicker : ComboBox
{
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
