using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat;

public readonly record struct CharacterAttributes(
	int HpCap,
	int PhysicalAttack,
	int PhysicalDefense,
	int MagicAttack,
	int MagicDefense,
	int HealPower,
	int PhysicalShield,
	int MagicShield,
	int DamageScale,
	int DamageTakenScale,
	int Taunt,
	int DrawCount,
	int MaxEnergy,
	int InitialEnergy)
{
	public static CharacterAttributes Zero { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

	public static CharacterAttributes FromCardStats(CardStatBlockDto stats) => new(
		stats.HpCap,
		stats.PhysicalAttack,
		stats.PhysicalDefense,
		stats.MagicAttack,
		stats.MagicDefense,
		stats.HealPower,
		stats.PhysicalShield,
		stats.MagicShield,
		stats.DamageScale,
		stats.DamageTakenScale,
		stats.Taunt,
		stats.DrawCount,
		stats.MaxEnergy,
		stats.InitialEnergy);

	public static CharacterAttributes operator +(CharacterAttributes left, CharacterAttributes right) => new(
		left.HpCap + right.HpCap,
		left.PhysicalAttack + right.PhysicalAttack,
		left.PhysicalDefense + right.PhysicalDefense,
		left.MagicAttack + right.MagicAttack,
		left.MagicDefense + right.MagicDefense,
		left.HealPower + right.HealPower,
		left.PhysicalShield + right.PhysicalShield,
		left.MagicShield + right.MagicShield,
		left.DamageScale + right.DamageScale,
		left.DamageTakenScale + right.DamageTakenScale,
		left.Taunt + right.Taunt,
		left.DrawCount + right.DrawCount,
		left.MaxEnergy + right.MaxEnergy,
		left.InitialEnergy + right.InitialEnergy);
}
