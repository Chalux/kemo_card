using KemoCard.Frame.Content;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentModDiscoveryTests
{
	[Test]
	public void Scan_skips_duplicate_modId()
	{
		var root = Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
		ContentModTestHelper.CreateModFolder(root, "a", "dup.mod");
		ContentModTestHelper.CreateModFolder(root, "b", "dup.mod");

		var discovery = new ContentModDiscovery();
		var result = discovery.Scan(root);

		Assert.That(result.ValidMods, Has.Count.EqualTo(1));
		Assert.That(result.SkippedMods, Has.Some.Matches<ModSkipEntry>(e =>
			e.Reason == ModSkipReason.DuplicateModId));
	}
}
