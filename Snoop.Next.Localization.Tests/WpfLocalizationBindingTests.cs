namespace Snoop.Next.Localization.Tests;

using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using NUnit.Framework;

/// <summary>Live WPF property tests: real STA XAML plus Dispatcher, no injection.</summary>
[TestFixture]
[NonParallelizable]
public sealed class WpfLocalizationBindingTests
{
    private const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'";
    private const string LocNs = "xmlns:loc='clr-namespace:Snoop.Next.Localization;assembly=Snoop.Next.Localization'";

    [Test]
    [Apartment(ApartmentState.STA)]
    public void TextBlockMarkup_RebindsOnDispatcherWithoutChangingCulture()
    {
        var manager = LocalizationManager.Default;
        var cultureBefore = Thread.CurrentThread.CurrentUICulture;
        try
        {
            manager.SetLanguage("zh-CN");
            var control = (TextBlock)XamlReader.Parse(
                "<TextBlock " + Ns + " " + LocNs + " Text='{loc:Loc Process.Attach}' />");
            DrainDispatcher();
            Assert.That(control.Text, Is.EqualTo("附加"));
            manager.SetLanguage("en-US");
            DrainDispatcher();
            Assert.That(control.Text, Is.EqualTo("Attach"));
            Assert.That(Thread.CurrentThread.CurrentUICulture, Is.EqualTo(cultureBefore));
        }
        finally
        {
            manager.SetLanguage("zh-CN");
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void MenuItemHeaderMarkup_UpdatesWhenLanguageChanges()
    {
        var manager = LocalizationManager.Default;
        try
        {
            manager.SetLanguage("zh-CN");
            var item = (MenuItem)XamlReader.Parse(
                "<MenuItem " + Ns + " " + LocNs + " Header='{loc:Loc Menu.File}' />");
            DrainDispatcher();
            Assert.That(item.Header, Is.EqualTo("文件"));
            manager.SetLanguage("en-US");
            DrainDispatcher();
            Assert.That(item.Header, Is.EqualTo("File"));
        }
        finally
        {
            manager.SetLanguage("zh-CN");
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void DataTemplateContent_UpdatesWithoutReloadingVisualTree()
    {
        var manager = LocalizationManager.Default;
        try
        {
            manager.SetLanguage("zh-CN");
            var template = (DataTemplate)XamlReader.Parse(
                "<DataTemplate " + Ns + " " + LocNs + "><TextBlock Text='{loc:Loc Nav.Diagnostics}' /></DataTemplate>");
            var text = (TextBlock)template.LoadContent();
            DrainDispatcher();
            Assert.That(text.Text, Is.EqualTo("诊断"));
            manager.SetLanguage("en-US");
            DrainDispatcher();
            Assert.That(text.Text, Is.EqualTo("Diagnostics"));
        }
        finally
        {
            manager.SetLanguage("zh-CN");
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void StyleSetterAttachedKey_UpdatesAndCanBeDetached()
    {
        var manager = LocalizationManager.Default;
        try
        {
            manager.SetLanguage("zh-CN");
            var style = (Style)XamlReader.Parse(
                "<Style " + Ns + " " + LocNs
                + " xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'"
                + " TargetType='{x:Type TextBlock}'>"
                + "<Setter Property='loc:LocText.Key' Value='Tree.Title' />"
                + "</Style>");
            var text = new TextBlock { Style = style };
            DrainDispatcher();
            Assert.That(text.Text, Is.EqualTo("可视化树"));
            manager.SetLanguage("en-US");
            DrainDispatcher();
            Assert.That(text.Text, Is.EqualTo("Visual tree"));

            text.Style = null;
            DrainDispatcher();
            text.Text = "custom";
            manager.SetLanguage("zh-CN");
            DrainDispatcher();
            Assert.That(text.Text, Is.EqualTo("custom"));
        }
        finally
        {
            manager.SetLanguage("zh-CN");
        }
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
