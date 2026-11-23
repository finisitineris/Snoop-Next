namespace Snoop.Views.TriggersTab.Triggers;

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Snoop.Localization;

public class EventTriggerItem : TriggerItemBase
{
    private readonly EventTrigger eventTrigger;

    public EventTriggerItem(EventTrigger eventTrigger, DependencyObject source, TriggerSource triggerSource)
        : base(eventTrigger, source, TriggerType.EventTrigger, triggerSource)
    {
        this.eventTrigger = eventTrigger;
    }

    private string GetDisplayName()
    {
        return string.Format(CultureInfo.CurrentCulture, InspectorLocalization.Default["Triggers.EventCondition"],
            this.eventTrigger.SourceName, this.eventTrigger.RoutedEvent.Name);
    }

    /// <inheritdoc />
    protected override IEnumerable<ConditionItem> GetConditions()
    {
        yield return new ConditionItem(this.Instance, null, this.GetDisplayName());
    }

    /// <inheritdoc />
    protected override IEnumerable<SetterItem> GetSetters()
    {
        return Enumerable.Empty<SetterItem>();
    }

    /// <inheritdoc />
    protected override IEnumerable<TriggerActionItem> GetEnterActions()
    {
        return this.eventTrigger.Actions.Select(x => TriggerActionItemFactory.GetTriggerActionItem(x, this.Instance, this.TriggerSource))
            .Concat(base.GetEnterActions());
    }
}
