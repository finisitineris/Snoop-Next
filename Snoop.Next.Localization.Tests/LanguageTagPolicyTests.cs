namespace Snoop.Next.Localization.Tests;

using System.Globalization;
using System.Threading;
using NUnit.Framework;

[TestFixture]
public sealed class LanguageTagPolicyTests
{
    [TestCase(null, "zh-CN")]
    [TestCase("", "zh-CN")]
    [TestCase("unknown-tag", "zh-CN")]
    [TestCase("EN-us", "en-US")]
    [TestCase("zh-cn", "zh-CN")]
    [TestCase("SYSTEM", "system")]
    public void ValuesAreCanonicalAndLegacySafe(string? input, string expected)
    {
        var original = Thread.CurrentThread.CurrentUICulture;
        Assert.That(LanguageTagPolicy.Normalize(input), Is.EqualTo(expected));
        Assert.That(Thread.CurrentThread.CurrentUICulture, Is.EqualTo(original));
    }
}
