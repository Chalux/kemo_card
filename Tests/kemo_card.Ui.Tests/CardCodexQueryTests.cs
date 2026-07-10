using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class CardCodexQueryTests
{
	private static CardDto Card(
		string id = "c1",
		ECardType type = ECardType.Physics,
		int cost = 1,
		ECostType costType = ECostType.Energy,
		int element = (int)EElement.Red,
		ERole role = ERole.Warrior,
		IEnumerable<string>? tags = null,
		bool hideInDex = false,
		string displayNameId = "card.c1.name",
		IEnumerable<string>? skillIds = null) => new()
	{
		Id = id,
		DisplayNameId = displayNameId,
		CardType = type,
		Cost = cost,
		CostType = costType,
		Element = element,
		Role = role,
		Tags = tags?.ToList() ?? [],
		HideInDex = hideInDex,
		SkillRefs = (skillIds ?? []).Select(s => new SkillRefDto { SkillId = s }).ToList(),
	};

	[Test]
	public void MatchesCondition_card_type_equal_and_not_equal()
	{
		var card = Card(type: ECardType.Physics);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.CardType, ECardFilterOp.Equal, nameof(ECardType.Physics), "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.CardType, ECardFilterOp.Equal, nameof(ECardType.Magical), "")), Is.False);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.CardType, ECardFilterOp.NotEqual, nameof(ECardType.Magical), "")), Is.True);
	}

	[Test]
	public void MatchesCondition_cost_comparisons()
	{
		var card = Card(cost: 2);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Cost, ECardFilterOp.Equal, "2", "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Cost, ECardFilterOp.LessOrEqual, "2", "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Cost, ECardFilterOp.LessOrEqual, "1", "")), Is.False);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Cost, ECardFilterOp.GreaterOrEqual, "2", "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Cost, ECardFilterOp.GreaterOrEqual, "3", "")), Is.False);
	}

	[Test]
	public void MatchesCondition_element_contains_and_exact()
	{
		var multi = Card(element: (int)(EElement.Red | EElement.Blue));
		Assert.That(CardCodexQuery.MatchesCondition(multi, new(ECardFilterField.Element, ECardFilterOp.Contains, nameof(EElement.Red), "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(multi, new(ECardFilterField.Element, ECardFilterOp.Exact, nameof(EElement.Red), "")), Is.False);
		var single = Card(element: (int)EElement.Red);
		Assert.That(CardCodexQuery.MatchesCondition(single, new(ECardFilterField.Element, ECardFilterOp.Exact, nameof(EElement.Red), "")), Is.True);
	}

	[Test]
	public void MatchesCondition_tag_contains_and_exact()
	{
		var card = Card(tags: ["attack", "basic"]);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Tag, ECardFilterOp.Contains, "attack", "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Tag, ECardFilterOp.Exact, "attack", "")), Is.False);
		var only = Card(tags: ["attack"]);
		Assert.That(CardCodexQuery.MatchesCondition(only, new(ECardFilterField.Tag, ECardFilterOp.Exact, "attack", "")), Is.True);
	}

	[Test]
	public void MatchesCondition_role_and_cost_type()
	{
		var card = Card(role: ERole.Healer, costType: ECostType.Health);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Role, ECardFilterOp.Equal, nameof(ERole.Healer), "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Role, ECardFilterOp.NotEqual, nameof(ERole.Warrior), "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.CostType, ECardFilterOp.Equal, nameof(ECostType.Health), "")), Is.True);
	}

	[Test]
	public void Filter_excludes_hide_in_dex_and_ands_conditions()
	{
		var cards = new Dictionary<string, CardDto>
		{
			["a"] = Card(id: "a", type: ECardType.Physics, cost: 1),
			["b"] = Card(id: "b", type: ECardType.Physics, cost: 3),
			["c"] = Card(id: "c", type: ECardType.Magical, cost: 1),
			["h"] = Card(id: "h", type: ECardType.Physics, cost: 1, hideInDex: true),
		};

		var conditions = new[]
		{
			new CardFilterCondition(ECardFilterField.CardType, ECardFilterOp.Equal, nameof(ECardType.Physics), ""),
			new CardFilterCondition(ECardFilterField.Cost, ECardFilterOp.LessOrEqual, "2", ""),
		};

		var result = CardCodexQuery.Filter(cards.Values, conditions, textQuery: "", _ => "", _ => null);
		Assert.That(result.Select(c => c.Id), Is.EqualTo(new[] { "a" }));
	}

	[Test]
	public void Filter_text_matches_name_and_skill_desc()
	{
		var cards = new[]
		{
			Card(id: "n", displayNameId: "card.fire.name", skillIds: ["s1"]),
			Card(id: "d", displayNameId: "card.ice.name", skillIds: ["s2"]),
			Card(id: "x", displayNameId: "card.rock.name", skillIds: ["s3"]),
		};

		string Tr(string key) => key switch
		{
			"card.fire.name" => "火焰打击",
			"card.ice.name" => "寒冰护盾",
			"skill.s2.desc" => "造成火焰伤害",
			_ => key,
		};

		SkillDto? GetSkill(string id) => id switch
		{
			"s1" => new SkillDto { Id = "s1", DescId = "skill.s1.desc" },
			"s2" => new SkillDto { Id = "s2", DescId = "skill.s2.desc" },
			_ => null,
		};

		var byName = CardCodexQuery.Filter(cards, [], "火焰", Tr, GetSkill);
		Assert.That(byName.Select(c => c.Id), Is.EqualTo(new[] { "n", "d" }).AsCollection);
		// "火焰" 命中卡名「火焰打击」与技能描述「造成火焰伤害」

		var empty = CardCodexQuery.Filter(cards, [], "  ", Tr, GetSkill);
		Assert.That(empty.Count, Is.EqualTo(3));
	}

	[Test]
	public void SlicePage_and_collect_tags()
	{
		var cards = Enumerable.Range(0, 10)
			.Select(i => Card(id: $"c{i:D2}", tags: i % 2 == 0 ? ["even", "shared"] : ["odd"]))
			.ToList();

		var page0 = CardCodexQuery.SlicePage(cards, page: 0, pageSize: 8);
		Assert.That(page0.Count, Is.EqualTo(8));
		Assert.That(page0[0].Id, Is.EqualTo("c00"));

		var page1 = CardCodexQuery.SlicePage(cards, page: 1, pageSize: 8);
		Assert.That(page1.Count, Is.EqualTo(2));

		Assert.That(CardCodexQuery.TotalPages(10, 8), Is.EqualTo(2));
		Assert.That(CardCodexQuery.TotalPages(0, 8), Is.EqualTo(0));

		var tags = CardCodexQuery.CollectTags(cards);
		Assert.That(tags, Is.EqualTo(new[] { "even", "odd", "shared" }));
	}
}
