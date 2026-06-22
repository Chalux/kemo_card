using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Rules;

public sealed class DamagePacket
{
	public CombatTargetRef Source { get; init; }
	public CombatTargetRef Target { get; init; }
	public float Amount { get; set; }
	public string? EffectId { get; init; }
}
