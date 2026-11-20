namespace Snoop.Localization;

using System;
using System.Windows.Data;
using System.Windows.Markup;

/// <summary>Live WPF binding to a Snoop-owned inspector resource key.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class InspectorLocExtension : MarkupExtension
{
    public InspectorLocExtension(string key)
    {
        this.Key = key ?? throw new ArgumentNullException(nameof(key));
    }

    [ConstructorArgument("key")]
    public string Key { get; }

    public Binding CreateBinding()
    {
        return new Binding("[" + this.Key + "]")
        {
            Source = InspectorLocalization.Default,
            Mode = BindingMode.OneWay
        };
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return this.CreateBinding().ProvideValue(serviceProvider);
    }
}
