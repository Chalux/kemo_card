using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class CardDescBuilderTests
{
	[Test]
	public void Build_joins_skill_descs_with_newline()
	{
		var card = new CardDto
		{
			SkillRefs =
			[
				new SkillRefDto { SkillId = "s1" },
				new SkillRefDto { SkillId = "s2" },
			],
		};

		var text = CardDescBuilder.Build(
			card,
			id => id switch
			{
				"s1" => new SkillDto { Id = "s1", DescId = "d1" },
				"s2" => new SkillDto { Id = "s2", DescId = "d2" },
				_ => null,
			},
			key => key switch
			{
				"d1" => "第一段",
				"d2" => "第二段",
				_ => key,
			});

		Assert.That(text, Is.EqualTo("第一段\n第二段"));
	}

	[Test]
	public void Build_skips_missing_skill_or_empty_desc()
	{
		var card = new CardDto
		{
			SkillRefs =
			[
				new SkillRefDto { SkillId = "missing" },
				new SkillRefDto { SkillId = "empty" },
				new SkillRefDto { SkillId = "ok" },
			],
		};

		var text = CardDescBuilder.Build(
			card,
			id => id switch
			{
				"empty" => new SkillDto { Id = "empty", DescId = "" },
				"ok" => new SkillDto { Id = "ok", DescId = "d" },
				_ => null,
			},
			key => key == "d" ? "有效" : key);

		Assert.That(text, Is.EqualTo("有效"));
	}

	[Test]
	public void TryParseKeywordMeta_parses_kw_prefix()
	{
		Assert.That(CardDescBuilder.TryParseKeywordMeta("kw:exhaust", out var id), Is.True);
		Assert.That(id, Is.EqualTo("exhaust"));
	}

	[Test]
	public void TryParseKeywordMeta_rejects_invalid()
	{
		Assert.That(CardDescBuilder.TryParseKeywordMeta("exhaust", out _), Is.False);
		Assert.That(CardDescBuilder.TryParseKeywordMeta("kw:", out _), Is.False);
		Assert.That(CardDescBuilder.TryParseKeywordMeta("", out _), Is.False);
		Assert.That(CardDescBuilder.TryParseKeywordMeta(null, out _), Is.False);
	}
}
