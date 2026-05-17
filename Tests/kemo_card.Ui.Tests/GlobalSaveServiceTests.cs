using KemoCard.Mod.Global.Save;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class GlobalSaveServiceTests
{
	[Test]
	public void RoundTrip_persists_global_save_fields()
	{
		var dir = Path.Combine(Path.GetTempPath(), "kemo_card_global_save_tests", Guid.NewGuid().ToString("N"));
		var io = new GlobalSaveService(dir);

		var original = new GlobalSaveDto(
			SchemaVersion: GlobalSaveDto.CurrentSchemaVersion,
			Achievements: new Dictionary<string, bool> { ["first_win"] = true },
			Unlocks: new Dictionary<string, bool> { ["hero_a"] = true },
			CodexEntries: new Dictionary<string, bool> { ["card_001"] = true },
			Settings: new Dictionary<string, string> { ["lang"] = "zh" },
			ContentVersionHash: "abc123");

		io.Save(original);
		var loaded = io.LoadOrDefault();

		Assert.That(loaded.SchemaVersion, Is.EqualTo(original.SchemaVersion));
		Assert.That(loaded.Achievements["first_win"], Is.True);
		Assert.That(loaded.Unlocks["hero_a"], Is.True);
		Assert.That(loaded.CodexEntries["card_001"], Is.True);
		Assert.That(loaded.Settings["lang"], Is.EqualTo("zh"));
		Assert.That(loaded.ContentVersionHash, Is.EqualTo("abc123"));
	}

	[Test]
	public void RoundTrip_persists_enabled_mod_ids()
	{
		var dir = Path.Combine(Path.GetTempPath(), "kemo_card_global_save_tests", Guid.NewGuid().ToString("N"));
		var io = new GlobalSaveService(dir);

		var original = GlobalSaveDto.CreateDefault() with
		{
			EnabledModIds = new[] { "base.game", "addon.mod" },
		};

		io.Save(original);
		var loaded = io.LoadOrDefault().Normalize();

		Assert.That(loaded.EnabledModIds, Is.EqualTo(new[] { "base.game", "addon.mod" }));
	}

	[Test]
	public void LoadOrDefault_without_file_returns_default()
	{
		var dir = Path.Combine(Path.GetTempPath(), "kemo_card_global_save_tests", Guid.NewGuid().ToString("N"));
		var io = new GlobalSaveService(dir);
		var loaded = io.LoadOrDefault();

		Assert.That(loaded.SchemaVersion, Is.EqualTo(GlobalSaveDto.CurrentSchemaVersion));
		Assert.That(loaded.Achievements, Is.Empty);
		Assert.That(loaded.Settings, Is.Empty);
		Assert.That(loaded.EnabledModIds, Is.EqualTo(new[] { "base.game" }));
	}
}
