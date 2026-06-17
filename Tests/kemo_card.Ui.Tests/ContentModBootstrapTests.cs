using KemoCard.Frame.Content;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentModBootstrapTests
{
	[Test]
	public void EnsureDefaultModsCopied_copies_when_destination_missing()
	{
		var root = Path.Combine(Path.GetTempPath(), "kemo_bootstrap_tests", Guid.NewGuid().ToString("N"));
		var bundled = Path.Combine(root, "bundled");
		var userMods = Path.Combine(root, "user-mods");
		ContentModTestHelper.CreateModFolder(bundled, "base-game", "base.game");

		ContentModBootstrap.EnsureDefaultModsCopied(userMods, bundled);

		Assert.That(File.Exists(Path.Combine(userMods, "base-game", "mod.json")), Is.True);
		Assert.That(Directory.Exists(Path.Combine(userMods, "base-game.staging")), Is.False);
	}

	[Test]
	public void EnsureDefaultModsCopied_recopies_when_bundled_version_changes()
	{
		var root = Path.Combine(Path.GetTempPath(), "kemo_bootstrap_tests", Guid.NewGuid().ToString("N"));
		var bundled = Path.Combine(root, "bundled");
		var userMods = Path.Combine(root, "user-mods");
		ContentModTestHelper.CreateModFolder(bundled, "base-game", "base.game", version: "1.0.0");

		ContentModBootstrap.EnsureDefaultModsCopied(userMods, bundled);

		var installedManifest = Path.Combine(userMods, "base-game", "mod.json");
		Assert.That(File.ReadAllText(installedManifest), Does.Contain("\"version\": \"1.0.0\""));

		File.WriteAllText(
			Path.Combine(bundled, "base-game", "mod.json"),
			File.ReadAllText(Path.Combine(bundled, "base-game", "mod.json")).Replace("1.0.0", "2.0.0"));

		ContentModBootstrap.EnsureDefaultModsCopied(userMods, bundled);

		Assert.That(File.ReadAllText(installedManifest), Does.Contain("\"version\": \"2.0.0\""));
	}

	[Test]
	public void EnsureDefaultModsCopied_does_not_leave_staging_directory()
	{
		var root = Path.Combine(Path.GetTempPath(), "kemo_bootstrap_tests", Guid.NewGuid().ToString("N"));
		var bundled = Path.Combine(root, "bundled");
		var userMods = Path.Combine(root, "user-mods");
		ContentModTestHelper.CreateModFolder(bundled, "base-game", "base.game");

		ContentModBootstrap.EnsureDefaultModsCopied(userMods, bundled);

		Assert.That(Directory.Exists(Path.Combine(userMods, "base-game.staging")), Is.False);
	}
}
