namespace Snoop.Next.Localization;

using System;
using System.Windows.Data;
using System.Windows.Markup;

/// <summary>Resolves {local:Loc Shell.Title} using a live WPF indexer binding.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string key)
    {
        this.Key = key ?? throw new ArgumentNullException(nameof(key));
    }

    [ConstructorArgument("key")]
    public string Key { get; }

    /// <summary>Useful for testing and for WPF hosts that need an explicit binding.</summary>
    public Binding CreateBinding()
    {
        return new Binding("[" + this.Key + "]")
        {
            Source = LocalizationManager.Default,
            Mode = BindingMode.OneWay
        };
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return this.CreateBinding().ProvideValue(serviceProvider);
    }
}
