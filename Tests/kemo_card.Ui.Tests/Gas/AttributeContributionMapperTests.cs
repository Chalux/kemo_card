using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class AttributeContributionMapperTests
{
	[Test]
	public void MapCardStats_maps_legacy_hp_cap_to_max_health()
	{
		var stats = new CardStatBlockDto
		{
			HpCap = 4,
			PhysicalAttack = 6,
		};

		var mapped = AttributeContributionMapper.MapCardStats(stats);

		Assert.That(mapped[AttributeIds.MaxHealth], Is.EqualTo(4f));
		Assert.That(mapped[AttributeIds.PhysicalAttack], Is.EqualTo(6f));
	}

	[Test]
	public void BuildEnemyBaseAttributes_falls_back_to_max_hp()
	{
		var enemy = new EnemyDto
		{
			Id = "slime",
			MaxHp = 30,
		};

		var mapped = AttributeContributionMapper.BuildEnemyBaseAttributes(enemy);

		Assert.That(mapped[AttributeIds.MaxHealth], Is.EqualTo(30f));
	}
}
