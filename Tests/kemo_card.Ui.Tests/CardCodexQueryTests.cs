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
}
