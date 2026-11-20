namespace Snoop.Next.Localization.Tests;

using System;
using System.Globalization;
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


    [Test]
    [Apartment(ApartmentState.STA)]
    [Timeout(30000)]
    public void TwoDispatchers_RefreshLocalizedControlsWithoutChangingTheirCultureOrResources()
    {
        var manager = LocalizationManager.Default;
        try
        {
            manager.SetLanguage("zh-CN");
            using (var first = new DispatcherHost("Snoop-Loc-First"))
            using (var second = new DispatcherHost("Snoop-Loc-Second"))
            {
                var text = first.Invoke(() =>
                {
                    Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
                    var element = (TextBlock)XamlReader.Parse(
                        "<TextBlock " + Ns + " " + LocNs + " Text='{loc:Loc Process.Attach}' />");
                    element.Resources["Untouched"] = "first-marker";
                    return element;
                });
                var menu = second.Invoke(() =>
                {
                    Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
                    var element = (MenuItem)XamlReader.Parse(
                        "<MenuItem " + Ns + " " + LocNs + " Header='{loc:Loc Menu.File}' />");
                    element.Resources["Untouched"] = "second-marker";
                    return element;
                });

                AssertState("附加", "文件");
                foreach (var language in new[] { "en-US", "zh-CN", "en-US" })
                {
                    first.Invoke(() => manager.SetLanguage(language));
                    AssertState(language == "en-US" ? "Attach" : "附加",
                        language == "en-US" ? "File" : "文件");
                }

                void AssertState(string expectedText, string expectedMenu)
                {
                    var updated = SpinWait.SpinUntil(
                        () => first.Invoke(() => text.Text == expectedText)
                            && second.Invoke(() => string.Equals(menu.Header as string, expectedMenu, StringComparison.Ordinal)),
                        TimeSpan.FromSeconds(6));
                    Assert.That(updated, Is.True, "WPF did not propagate the indexer update across dispatchers");
                    Assert.That(first.Invoke(() => Thread.CurrentThread.CurrentUICulture.Name), Is.EqualTo("ja-JP"));
                    Assert.That(second.Invoke(() => Thread.CurrentThread.CurrentUICulture.Name), Is.EqualTo("fr-FR"));
                    Assert.That(first.Invoke(() => text.Resources["Untouched"]), Is.EqualTo("first-marker"));
                    Assert.That(second.Invoke(() => menu.Resources["Untouched"]), Is.EqualTo("second-marker"));
                }
            }
        }
        finally
        {
            manager.SetLanguage("zh-CN");
        }
    }

    private sealed class DispatcherHost : IDisposable
    {
        private readonly ManualResetEventSlim ready = new ManualResetEventSlim();
        private readonly Thread worker;
        private Dispatcher? dispatcher;
        private Exception? startupFailure;

        public DispatcherHost(string name)
        {
            this.worker = new Thread(this.Run) { IsBackground = true, Name = name };
            this.worker.SetApartmentState(ApartmentState.STA);
            this.worker.Start();
            if (!this.ready.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Dispatcher did not start");
            }

            if (this.startupFailure != null)
            {
                throw new InvalidOperationException("Dispatcher failed", this.startupFailure);
            }
        }

        public T Invoke<T>(Func<T> action)
        {
            var current = this.dispatcher ?? throw new InvalidOperationException("Dispatcher not started");
            return current.Invoke(action);
        }

        public void Invoke(Action action)
        {
            var current = this.dispatcher ?? throw new InvalidOperationException("Dispatcher not started");
            current.Invoke(action);
        }

        public void Dispose()
        {
            var current = this.dispatcher;
            if (current != null && !current.HasShutdownStarted && !current.HasShutdownFinished)
            {
                current.BeginInvokeShutdown(DispatcherPriority.Send);
            }

            if (!this.worker.Join(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Dispatcher did not shut down");
            }

            this.ready.Dispose();
        }

        private void Run()
        {
            try
            {
                this.dispatcher = Dispatcher.CurrentDispatcher;
                this.ready.Set();
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                this.startupFailure = exception;
                this.ready.Set();
            }
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
