namespace Snoop.Views;

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using Snoop.Data.Tree;
using Snoop.Infrastructure.Helpers;
using Snoop.Localization;

public partial class ElementDetailsView
{
    private static readonly Attribute[] allProperties = { new PropertyFilterAttribute(PropertyFilterOptions.All) };

    private static readonly DependencyProperty LocalizedLanguageProperty = DependencyProperty.Register(
        "LocalizedLanguage", typeof(string), typeof(ElementDetailsView),
        new PropertyMetadata(null, (d, _) => ((ElementDetailsView)d).Refresh(languageChanged: true)));

    private string messageKey = "SelectionDetails.NoBindings";

    public ElementDetailsView()
    {
        this.InitializeComponent();
        this.SetBinding(LocalizedLanguageProperty, new Binding(nameof(InspectorLocalization.EffectiveLanguageTag))
        {
            Source = InspectorLocalization.Default,
            Mode = BindingMode.OneWay
        });
        this.DataContextChanged += (_, _) => this.Refresh();
        this.IsVisibleChanged += (_, _) => this.Refresh();
    }

    public bool ShowRoutedEvents { get; set; }

    private void Refresh(bool languageChanged = false)
    {
        this.BindingsPanel.Visibility = this.ShowRoutedEvents ? Visibility.Collapsed : Visibility.Visible;
        this.RoutedEventsPanel.Visibility = this.ShowRoutedEvents ? Visibility.Visible : Visibility.Collapsed;
        var list = this.ShowRoutedEvents ? this.RoutedEventsList : this.BindingsList;
        var message = this.ShowRoutedEvents ? this.RoutedEventsEmpty : this.BindingsEmpty;
        if (languageChanged)
        {
            message.Text = InspectorLocalization.Default[this.messageKey];
            if (!this.IsVisible || this.ShowRoutedEvents || list.ItemsSource is not List<BindingRow>)
            {
                return;
            }
        }

        this.messageKey = this.ShowRoutedEvents ? "SelectionDetails.NoRoutedEvents" : "SelectionDetails.NoBindings";

        // Keep the application's target out of the view's inherited DataContext.
        // F5 rebuilds the inspector's TreeItem even when its Target stays the same.
        var selection = this.DataContext as TreeItem;
        IEnumerable? rows = null;
        if (this.IsVisible && selection?.Target is DependencyObject target)
        {
            try
            {
                if (this.ShowRoutedEvents)
                {
                    if (target is UIElement or ContentElement or UIElement3D)
                    {
                        rows = GetEvents(target.GetType());
                    }
                }
                else
                {
                    target.VerifyAccess();
                    rows = GetBindings(target, languageChanged ? list.ItemsSource as List<BindingRow> : null);
                }
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException
                                                  or AppDomainUnloadedException or ThreadAbortException or SEHException))
            {
                // Custom type description providers can fail even when no property value is requested.
                this.messageKey = this.ShowRoutedEvents ? "SelectionDetails.RoutedEventsUnavailable" : "SelectionDetails.BindingsUnavailable";
            }
        }

        list.ItemsSource = rows;
        message.Text = InspectorLocalization.Default[this.messageKey];
    }

    private static List<BindingRow> GetBindings(DependencyObject target, List<BindingRow>? previousRows)
    {
        var rows = new List<BindingRow>();
        if (previousRows is not null)
        {
            // Only descriptions change language. Reuse the known DP identities without
            // keeping inspected objects/expressions alive or enumerating their descriptors again.
            foreach (var row in previousRows)
            {
                AddBinding(row.Property, row.PropertyName);
            }

            return rows;
        }

        var seen = new HashSet<DependencyProperty>();
        // WPF's descriptor provider includes attached and style-supplied DPs.
        // Read binding metadata only; reading descriptor/effective values can run application code.
        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(target, allProperties, noCustomTypeDesc: true))
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(property);
            if (descriptor is null || descriptor.DependencyProperty.ReadOnly || !seen.Add(descriptor.DependencyProperty))
            {
                continue;
            }

            var dp = descriptor.DependencyProperty;
            AddBinding(dp, descriptor.IsAttached ? dp.OwnerType.Name + "." + dp.Name : dp.Name);
        }

        rows.Sort((left, right) => StringComparer.Ordinal.Compare(left.PropertyName, right.PropertyName));
        return rows;

        void AddBinding(DependencyProperty dp, string propertyName)
        {
            if (DependencyPropertyHelper.GetValueSource(target, dp).IsExpression
                && BindingOperations.GetBindingExpressionBase(target, dp) is { } expression)
            {
                rows.Add(new BindingRow(dp, propertyName, PropertyOriginFormatter.GetBindingDetails(expression)));
            }
        }
    }

    private static IEnumerable<EventRow> GetEvents(Type targetType)
    {
        var events = new HashSet<RoutedEvent>();
        for (var type = targetType; type is not null && typeof(DependencyObject).IsAssignableFrom(type); type = type.BaseType)
        {
            if (EventManager.GetRoutedEventsForOwner(type) is { } registered)
            {
                events.UnionWith(registered);
            }
        }

        return events.OrderBy(e => e.Name, StringComparer.Ordinal).ThenBy(e => e.OwnerType.FullName, StringComparer.Ordinal)
                     .Select(e => new EventRow(e)).ToArray();
    }

    private sealed class BindingRow
    {
        public BindingRow(DependencyProperty property, string propertyName, string description)
        {
            this.Property = property;
            this.PropertyName = propertyName;
            this.Description = description;
        }

        public DependencyProperty Property { get; }

        public string PropertyName { get; }

        public string Description { get; }

        public override string ToString() => this.PropertyName;
    }

    private sealed class EventRow
    {
        public EventRow(RoutedEvent routedEvent)
        {
            this.EventName = routedEvent.Name;
            var owner = routedEvent.OwnerType.FullName ?? routedEvent.OwnerType.Name;
            this.Description = $"{owner} · {routedEvent.RoutingStrategy}";
        }

        public string EventName { get; }

        public string Description { get; }

        public override string ToString() => this.EventName;
    }
}
