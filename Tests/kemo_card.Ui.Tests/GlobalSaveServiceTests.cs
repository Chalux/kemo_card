using System.Text.Json;
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

	[Test]
	public void Save_uses_replace_without_deleting_primary_first()
	{
		var dir = Path.Combine(Path.GetTempPath(), "kemo_card_global_save_tests", Guid.NewGuid().ToString("N"));
		var io = new GlobalSaveService(dir);

		var first = GlobalSaveDto.CreateDefault() with { ContentVersionHash = "v1" };
		var second = GlobalSaveDto.CreateDefault() with { ContentVersionHash = "v2" };

		io.Save(first);
		io.Save(second);

		Assert.That(File.Exists(Path.Combine(dir, "global_save.json")), Is.True);
		Assert.That(File.Exists(Path.Combine(dir, "global_save.bak.json")), Is.True);
		Assert.That(io.LoadOrDefault().ContentVersionHash, Is.EqualTo("v2"));
	}

	[Test]
	public void Concurrent_save_serializes()
	{
		var dir = Path.Combine(Path.GetTempPath(), "kemo_card_global_save_tests", Guid.NewGuid().ToString("N"));
		var io = new GlobalSaveService(dir);
		var errors = new List<Exception>();

		var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
		{
			try
			{
				for (var j = 0; j < 20; j++)
				{
					io.Save(GlobalSaveDto.CreateDefault() with { ContentVersionHash = $"t{i}-{j}" });
					_ = io.LoadOrDefault();
				}
			}
			catch (Exception ex)
			{
				lock (errors)
				{
					errors.Add(ex);
				}
			}
		})).ToArray();

		Task.WaitAll(tasks);
		Assert.That(errors, Is.Empty);
		Assert.That(io.LoadOrDefault().SchemaVersion, Is.EqualTo(GlobalSaveDto.CurrentSchemaVersion));
	}

	[Test]
	public void LoadOrDefault_corrupt_primary_falls_back_to_backup()
	{
		var dir = Path.Combine(Path.GetTempPath(), "kemo_card_global_save_tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		var primaryPath = Path.Combine(dir, "global_save.json");
		var backupPath = Path.Combine(dir, "global_save.bak.json");

		var valid = GlobalSaveDto.CreateDefault() with { ContentVersionHash = "from_backup" };
		File.WriteAllText(backupPath, JsonSerializer.Serialize(valid, new JsonSerializerOptions
		{
			WriteIndented = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		}));
		File.WriteAllText(primaryPath, "{ not valid json");

		var warnings = new List<string>();
		var io = new GlobalSaveService(dir, warnings.Add);
		var loaded = io.LoadOrDefault();

		Assert.That(loaded.ContentVersionHash, Is.EqualTo("from_backup"));
		Assert.That(warnings, Is.Not.Empty);
		Assert.That(Directory.EnumerateFiles(dir, "global_save.corrupt.*.json").Any(), Is.True);
	}
}
