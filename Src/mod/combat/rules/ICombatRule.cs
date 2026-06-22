namespace KemoCard.Mod.Combat.Rules;

public interface ICombatRule
{
	string Id { get; }
	int Priority { get; }

	void OnBattleStart(CombatContext ctx) { }
	void OnTurnStart(CombatContext ctx) { }
	void OnTurnEnd(CombatContext ctx) { }
	void OnBeforeDamage(CombatContext ctx, ref DamagePacket packet) { }
	void OnAfterDamage(CombatContext ctx, in DamagePacket packet) { }
	void OnCheckEndCondition(CombatContext ctx, ref EndDecision decision) { }
}
