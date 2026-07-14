using System.Linq;
using KemoCard.Frame.Display;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ResolutionRegistryTests
{
	[SetUp]
	public void SetUp() => ResolutionRegistry.ResetToBuiltinsForTests();

	[Test]
	public void Builtins_four_entries_labeled_as_wxh()
	{
		var all = ResolutionRegistry.All;
		Assert.That(all.Select(e => e.Id), Is.EqualTo(new[]
		{
			"1280x720", "1920x1080", "2560x1440", "3840x2160"
		}));
		Assert.That(all[0].Width, Is.EqualTo(1280));
		Assert.That(all[0].DisplayLabel, Is.EqualTo("1280x720"));
	}

	[Test]
	public void Register_appends_custom_entry()
	{
		ResolutionRegistry.Register(new ResolutionEntry("1600x900", 1600, 900));
		Assert.That(ResolutionRegistry.TryGet("1600x900", out var e), Is.True);
		Assert.That(e.Height, Is.EqualTo(900));
	}
}
