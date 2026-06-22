using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Gas;

namespace KemoCard.Mod.Combat.Runtime;

public sealed class PlayerTeamState
{
	private readonly CharacterBattleInstance[] _characters;

	public AbilitySystemComponent Asc { get; }
	public int SharedHp => (int)MathF.Round(Asc.GetCurrentValue(AttributeIds.Health));
	public int MaxHp => (int)MathF.Round(Asc.GetCurrentValue(AttributeIds.MaxHealth));
	public CombatDomain? ActiveDomain { get; set; }
	public IReadOnlyList<CharacterBattleInstance> Characters => _characters;
	public bool IsDefeated => SharedHp <= 0;

	public PlayerTeamState(
		IReadOnlyList<CharacterBattleInstance> characters,
		int sharedMaxHp,
		AbilitySystemComponent? teamAsc = null)
	{
		ArgumentNullException.ThrowIfNull(characters);
		if (characters.Count == 0)
			throw new ArgumentException("至少一名角色。", nameof(characters));
		if (sharedMaxHp <= 0)
			throw new ArgumentOutOfRangeException(nameof(sharedMaxHp));
		_characters = characters.ToArray();
		Asc = teamAsc ?? new CombatAscFactory().CreateTeamAsc(
			new Dictionary<string, AttributeDefDto>(StringComparer.Ordinal),
			sharedMaxHp);
	}

	public void ApplySharedDamage(int amount)
	{
		if (amount <= 0 || IsDefeated)
			return;
		Asc.Attributes.SetCurrentValue(AttributeIds.Health, Math.Max(0, SharedHp - amount));
	}

	public void HealShared(int amount)
	{
		if (amount <= 0 || IsDefeated)
			return;
		Asc.Attributes.SetCurrentValue(AttributeIds.Health, Math.Min(MaxHp, SharedHp + amount));
	}
}
