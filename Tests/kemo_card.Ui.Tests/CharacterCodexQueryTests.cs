using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class CharacterCodexQueryTests
{
	private static CharacterDto Character(
		string id = "ch1",
		EElement element = EElement.Red,
		ERole role = ERole.Warrior,
		ERace race = ERace.Human,
		IEnumerable<string>? tags = null,
		string displayNameId = "char.ch1.name",
		string descId = "char.ch1.desc",
		IEnumerable<string>? skillIds = null) => new()
	{
		Id = id,
		DisplayNameId = displayNameId,
		DescId = descId,
		Element = element,
		Role = role,
		Race = race,
		Tags = tags?.ToList() ?? [],
		SkillRefs = (skillIds ?? []).Select(s => new SkillRefDto { SkillId = s }).ToList(),
	};

	[Test]
	public void Race_contains_and_exact()
	{
		var c = new CharacterDto { Id = "a", Race = ERace.Human | ERace.Canine };
		Assert.That(CharacterCodexQuery.MatchesCondition(c, new(ECharFilterField.Race, ECardFilterOp.Contains, nameof(ERace.Human), "")), Is.True);
		Assert.That(CharacterCodexQuery.MatchesCondition(c, new(ECharFilterField.Race, ECardFilterOp.Exact, nameof(ERace.Human), "")), Is.False);
		var single = new CharacterDto { Id = "b", Race = ERace.Human };
		Assert.That(CharacterCodexQuery.MatchesCondition(single, new(ECharFilterField.Race, ECardFilterOp.Exact, nameof(ERace.Human), "")), Is.True);
	}

	[Test]
	public void MatchesCondition_element_contains_and_exact()
	{
		var multi = Character(element: EElement.Red | EElement.Blue);
		Assert.That(CharacterCodexQuery.MatchesCondition(multi, new(ECharFilterField.Element, ECardFilterOp.Contains, nameof(EElement.Red), "")), Is.True);
		Assert.That(CharacterCodexQuery.MatchesCondition(multi, new(ECharFilterField.Element, ECardFilterOp.Exact, nameof(EElement.Red), "")), Is.False);
		var single = Character(element: EElement.Red);
		Assert.That(CharacterCodexQuery.MatchesCondition(single, new(ECharFilterField.Element, ECardFilterOp.Exact, nameof(EElement.Red), "")), Is.True);
	}

	[Test]
	public void MatchesCondition_role_equal_and_not_equal()
	{
		var character = Character(role: ERole.Healer);
		Assert.That(CharacterCodexQuery.MatchesCondition(character, new(ECharFilterField.Role, ECardFilterOp.Equal, nameof(ERole.Healer), "")), Is.True);
		Assert.That(CharacterCodexQuery.MatchesCondition(character, new(ECharFilterField.Role, ECardFilterOp.Equal, nameof(ERole.Warrior), "")), Is.False);
		Assert.That(CharacterCodexQuery.MatchesCondition(character, new(ECharFilterField.Role, ECardFilterOp.NotEqual, nameof(ERole.Warrior), "")), Is.True);
	}

	[Test]
	public void MatchesCondition_tag_contains_and_exact()
	{
		var character = Character(tags: ["hero", "starter"]);
		Assert.That(CharacterCodexQuery.MatchesCondition(character, new(ECharFilterField.Tag, ECardFilterOp.Contains, "hero", "")), Is.True);
		Assert.That(CharacterCodexQuery.MatchesCondition(character, new(ECharFilterField.Tag, ECardFilterOp.Exact, "hero", "")), Is.False);
		var only = Character(tags: ["hero"]);
		Assert.That(CharacterCodexQuery.MatchesCondition(only, new(ECharFilterField.Tag, ECardFilterOp.Exact, "hero", "")), Is.True);
	}

	[Test]
	public void Filter_ands_conditions()
	{
		var characters = new Dictionary<string, CharacterDto>
		{
			["a"] = Character(id: "a", element: EElement.Red, role: ERole.Warrior),
			["b"] = Character(id: "b", element: EElement.Red, role: ERole.Healer),
			["c"] = Character(id: "c", element: EElement.Blue, role: ERole.Warrior),
		};

		var conditions = new[]
		{
			new CharFilterCondition(ECharFilterField.Element, ECardFilterOp.Contains, nameof(EElement.Red), ""),
			new CharFilterCondition(ECharFilterField.Role, ECardFilterOp.Equal, nameof(ERole.Warrior), ""),
		};

		var result = CharacterCodexQuery.Filter(characters.Values, conditions, textQuery: "", _ => "", _ => null);
		Assert.That(result.Select(c => c.Id), Is.EqualTo(new[] { "a" }));
	}

	[Test]
	public void Filter_text_matches_name_desc_and_skill_desc()
	{
		var characters = new[]
		{
			Character(id: "n", displayNameId: "char.fire.name", descId: "char.fire.desc", skillIds: ["s1"]),
			Character(id: "d", displayNameId: "char.ice.name", descId: "char.ice.desc", skillIds: ["s2"]),
			Character(id: "x", displayNameId: "char.rock.name", descId: "char.rock.desc", skillIds: ["s3"]),
		};

		string Tr(string key) => key switch
		{
			"char.fire.name" => "火焰使者",
			"char.ice.desc" => "掌控寒冰之力",
			"skill.s2.desc" => "造成火焰伤害",
			_ => key,
		};

		SkillDto? GetSkill(string id) => id switch
		{
			"s1" => new SkillDto { Id = "s1", DescId = "skill.s1.desc" },
			"s2" => new SkillDto { Id = "s2", DescId = "skill.s2.desc" },
			_ => null,
		};

		var byName = CharacterCodexQuery.Filter(characters, [], "火焰", Tr, GetSkill);
		Assert.That(byName.Select(c => c.Id), Is.EqualTo(new[] { "d", "n" }).AsCollection);

		var byDesc = CharacterCodexQuery.Filter(characters, [], "寒冰", Tr, GetSkill);
		Assert.That(byDesc.Select(c => c.Id), Is.EqualTo(new[] { "d" }));

		var empty = CharacterCodexQuery.Filter(characters, [], "  ", Tr, GetSkill);
		Assert.That(empty.Count, Is.EqualTo(3));
	}

	[Test]
	public void SlicePage_and_collect_tags()
	{
		var characters = Enumerable.Range(0, 10)
			.Select(i => Character(id: $"ch{i:D2}", tags: i % 2 == 0 ? ["even", "shared"] : ["odd"]))
			.ToList();

		var page0 = CharacterCodexQuery.SlicePage(characters, page: 0, pageSize: 8);
		Assert.That(page0.Count, Is.EqualTo(8));
		Assert.That(page0[0].Id, Is.EqualTo("ch00"));

		var page1 = CharacterCodexQuery.SlicePage(characters, page: 1, pageSize: 8);
		Assert.That(page1.Count, Is.EqualTo(2));

		Assert.That(CharacterCodexQuery.TotalPages(10, 8), Is.EqualTo(2));
		Assert.That(CharacterCodexQuery.TotalPages(0, 8), Is.EqualTo(0));

		var tags = CharacterCodexQuery.CollectTags(characters);
		Assert.That(tags, Is.EqualTo(new[] { "even", "odd", "shared" }));
	}

	[Test]
	public void OpsForField_returns_expected_ops()
	{
		Assert.That(CharacterCodexQuery.OpsForField(ECharFilterField.Element),
			Is.EqualTo(new[] { ECardFilterOp.Contains, ECardFilterOp.Exact }));
		Assert.That(CharacterCodexQuery.OpsForField(ECharFilterField.Race),
			Is.EqualTo(new[] { ECardFilterOp.Contains, ECardFilterOp.Exact }));
		Assert.That(CharacterCodexQuery.OpsForField(ECharFilterField.Role),
			Is.EqualTo(new[] { ECardFilterOp.Equal, ECardFilterOp.NotEqual }));
		Assert.That(CharacterCodexQuery.OpsForField(ECharFilterField.Tag),
			Is.EqualTo(new[] { ECardFilterOp.Contains, ECardFilterOp.Exact }));
		Assert.That(CharacterCodexQuery.PageSize, Is.EqualTo(8));
	}
}
