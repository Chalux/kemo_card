using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;

namespace KemoCard.Mod.Combat.Gas;

public sealed class CombatAscFactory
{
	public AbilitySystemComponent CreateCharacterAsc(
		IReadOnlyDictionary<string, AttributeDefDto> attributeDefs,
		IReadOnlyDictionary<string, float> contributions)
	{
		return BuildAsc(attributeDefs, contributions);
	}

	public AbilitySystemComponent CreateCharacterAsc(
		IReadOnlyDictionary<string, AttributeDefDto> attributeDefs,
		CharacterAttributes contributions)
	{
		return BuildAsc(attributeDefs, contributions.Values);
	}

	public AbilitySystemComponent CreateEnemyAsc(
		IReadOnlyDictionary<string, AttributeDefDto> attributeDefs,
		float maxHealth,
		float physicalAttack = 0f,
		float physicalDefense = 0f)
	{
		var asc = BuildAsc(
			attributeDefs,
			new Dictionary<string, float>(StringComparer.Ordinal)
			{
				[AttributeIds.MaxHealth] = maxHealth,
				[AttributeIds.PhysicalAttack] = physicalAttack,
				[AttributeIds.PhysicalDefense] = physicalDefense,
				[AttributeIds.Health] = maxHealth,
			});
		return asc;
	}

	public AbilitySystemComponent CreateTeamAsc(
		IReadOnlyDictionary<string, AttributeDefDto> attributeDefs,
		float maxHealth)
	{
		return BuildAsc(
			attributeDefs,
			new Dictionary<string, float>(StringComparer.Ordinal)
			{
				[AttributeIds.MaxHealth] = maxHealth,
				[AttributeIds.Health] = maxHealth,
			});
	}

	public AbilitySystemComponent BuildAsc(
		IReadOnlyDictionary<string, AttributeDefDto> attributeDefs,
		IReadOnlyDictionary<string, float> contributions)
	{
		ArgumentNullException.ThrowIfNull(attributeDefs);
		ArgumentNullException.ThrowIfNull(contributions);

		var asc = new AbilitySystemComponent();
		foreach (var (id, def) in attributeDefs)
			asc.Attributes.InitAttribute(id, def.DefaultBase);

		foreach (var (id, value) in contributions)
			asc.Attributes.SetBaseValue(id, asc.GetBaseValue(id) + value);

		return asc;
	}
}
