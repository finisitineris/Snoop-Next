namespace Snoop.Next.Localization.Tests;

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Windows.Data;
using NUnit.Framework;

[TestFixture]
public sealed class LocalizationTests
{
    [Test]
    public void SwitchingLanguagesChangesOnlyTheLocalizedValues()
    {
        var manager = new LocalizationManager("zh-CN");
        var changes = new List<string>();
        manager.PropertyChanged += (sender, args) => changes.Add(args.PropertyName ?? string.Empty);

        Assert.That(manager["Process.Attach"], Is.EqualTo("附加"));
        manager.SetLanguage("en-US");
        Assert.That(manager["Process.Attach"], Is.EqualTo("Attach"));
        manager.SetLanguage("zh-CN");
        Assert.That(manager["Process.Attach"], Is.EqualTo("附加"));
        Assert.That(changes.Count(x => x == "Item[]"), Is.EqualTo(2));
    }

    [TestCase("de-DE", "en-US", "Attach")]
    [TestCase("zh-CN", "zh-CN", "附加")]
    [TestCase("zh-TW", "zh-CN", "附加")]
    public void SystemPreferenceUsesTheSameResourceLanguageAsTheInspectorPayload(
        string cultureName, string expectedLanguageTag, string expectedLabel)
    {
        var original = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var manager = new LocalizationManager("system");

            Assert.That(manager.EffectiveLanguageTag, Is.EqualTo(expectedLanguageTag));
            Assert.That(manager["Process.Attach"], Is.EqualTo(expectedLabel));
            Assert.That(manager.UICulture.Name, Is.EqualTo(cultureName));
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = original;
        }
    }

    [Test]
    public void UnknownKeyIsDiagnosable()
    {
        var manager = new LocalizationManager("zh-CN");
        Assert.That(manager["Undefined.Translation.Key"], Is.EqualTo("[Missing: Undefined.Translation.Key]"));
    }

    [Test]
    public void LanguageSwitchDoesNotMutateThreadOrGlobalCulture()
    {
        var original = Thread.CurrentThread.CurrentUICulture;
        var defaultOriginal = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var manager = new LocalizationManager("zh-CN");
            manager.SetLanguage("en-US");
            manager.SetLanguage("zh-CN");
            Assert.That(Thread.CurrentThread.CurrentUICulture.Name, Is.EqualTo("de-DE"));
            Assert.That(CultureInfo.DefaultThreadCurrentUICulture, Is.SameAs(defaultOriginal));

            manager.SetLanguage("system");
            Assert.That(manager.UICulture.Name, Is.EqualTo("de-DE"));
            Assert.That(manager["Process.Attach"], Is.EqualTo("Attach"));
            Assert.That(Thread.CurrentThread.CurrentUICulture.Name, Is.EqualTo("de-DE"));
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = original;
        }
    }

    [Test]
    public void BothResourceSetsHaveIdenticalKeys()
    {
        var manager = new ResourceManager("Snoop.Next.Localization.Strings", typeof(LocalizationManager).Assembly);
        var neutral = manager.GetResourceSet(CultureInfo.InvariantCulture, true, true);
        var chinese = manager.GetResourceSet(CultureInfo.GetCultureInfo("zh-CN"), true, false);

        Assert.That(neutral, Is.Not.Null);
        Assert.That(chinese, Is.Not.Null);
        var enKeys = Keys(neutral!);
        var zhKeys = Keys(chinese!);
        Assert.That(zhKeys, Is.EquivalentTo(enKeys));
        Assert.That(enKeys, Does.Contain("Error.TargetExited"));
        Assert.That(enKeys, Does.Contain("Permission.AllowOnce"));
    }

    [Test]
    public void LocalizedFormattingUsesOnlySelectedUiCulture()
    {
        var manager = new LocalizationManager("en-US");
        Assert.That(manager.Format("Status.ElementsSelected", 12), Is.EqualTo("Selected elements: 12"));
        manager.SetLanguage("zh-CN");
        Assert.That(manager.Format("Status.ElementsSelected", 12), Is.EqualTo("已选中元素：12"));
    }

    [Test]
    public void LocMarkupCreatesOneWayIndexerBinding()
    {
        var extension = new LocExtension("Shell.Title");
        Binding binding = extension.CreateBinding();
        Assert.That(binding.Path.Path, Is.EqualTo("[Shell.Title]"));
        Assert.That(binding.Mode, Is.EqualTo(BindingMode.OneWay));
        Assert.That(binding.Source, Is.SameAs(LocalizationManager.Default));
    }

    private static string[] Keys(ResourceSet resources)
    {
        return resources.Cast<DictionaryEntry>()
            .Select(e => (string)e.Key)
            .OrderBy(key => key, StringComparer.Ordinal).ToArray();
    }
}
