using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class GameplayTagTests
{
	[Test]
	public void HasTag_matches_parent_tag()
	{
		var container = new GameplayTagContainer();
		container.AddTag("debuff.weak");
		Assert.That(container.HasTag("debuff"), Is.True);
		Assert.That(container.HasTag("debuff.weak"), Is.True);
		Assert.That(container.HasTag("buff"), Is.False);
	}

	[Test]
	public void HasTag_does_not_match_parent_from_child_query()
	{
		var container = new GameplayTagContainer();
		container.AddTag("debuff");
		Assert.That(container.HasTag("debuff.weak"), Is.False);
	}

	[Test]
	public void HasAny_and_HasAll_work()
	{
		var container = new GameplayTagContainer();
		container.AddTag("debuff.weak");
		Assert.That(container.HasAll(["debuff"]), Is.True);
		Assert.That(container.HasAny(["buff", "debuff"]), Is.True);
		Assert.That(container.HasAll(["debuff", "buff"]), Is.False);
		Assert.That(container.HasAny(["buff", "state"]), Is.False);
	}

	[Test]
	public void RemoveTag_removes_inherent_tag_only()
	{
		var container = new GameplayTagContainer();
		container.AddTag("state.combat");
		container.AddGrantedTag("debuff.poison");

		container.RemoveTag("state.combat");

		Assert.That(container.HasTag("state.combat"), Is.False);
		Assert.That(container.HasTag("debuff.poison"), Is.True);
	}

	[Test]
	public void ClearGrantedTags_preserves_inherent_tags()
	{
		var container = new GameplayTagContainer();
		container.AddTag("state.combat");
		container.AddGrantedTag("debuff.weak");

		container.ClearGrantedTags();

		Assert.That(container.HasTag("state.combat"), Is.True);
		Assert.That(container.HasTag("debuff.weak"), Is.False);
	}

	[Test]
	public void Granted_and_inherent_tags_both_contribute_to_matching()
	{
		var container = new GameplayTagContainer();
		container.AddTag("state.combat");
		container.AddGrantedTag("debuff.weak");

		Assert.That(container.HasAll(["state", "debuff"]), Is.True);
	}
}
