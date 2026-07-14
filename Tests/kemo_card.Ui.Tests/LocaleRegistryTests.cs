using System.Linq;
using KemoCard.Frame.Locale;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class LocaleRegistryTests
{
	[SetUp]
	public void SetUp() => LocaleRegistry.ResetToBuiltinsForTests();

	[Test]
	public void Builtins_zh_and_en()
	{
		Assert.That(LocaleRegistry.All.Select(e => e.Code), Is.EqualTo(new[] { "zh_CN", "en" }));
	}

	[Test]
	public void Register_appends_locale()
	{
		LocaleRegistry.Register(new LocaleEntry("ja", "UI_LOCALE_JA"));
		Assert.That(LocaleRegistry.TryGet("ja", out var e), Is.True);
		Assert.That(e.DisplayNameKey, Is.EqualTo("UI_LOCALE_JA"));
	}
}
