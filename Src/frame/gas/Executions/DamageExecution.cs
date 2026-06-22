using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Gas.Executions;

public sealed class DamageExecution : IExecutionCalculation
{
	public const string DamageKind = "Damage";
	private const string SetByCallerAmount = "Amount";

	public string Kind => DamageKind;

	public void Execute(ExecutionDefDto executionDef, GameplayEffectSpec spec, AbilitySystemComponent targetAsc)
	{
		ArgumentNullException.ThrowIfNull(executionDef);
		ArgumentNullException.ThrowIfNull(spec);
		ArgumentNullException.ThrowIfNull(targetAsc);

		var sourceAttack = spec.SourceAsc?.GetCurrentValue(AttributeIds.PhysicalAttack) ?? 0f;
		var sourceDamageBonus = spec.SourceAsc?.GetCurrentValue(AttributeIds.Damage) ?? 0f;
		var targetDefense = targetAsc.GetCurrentValue(AttributeIds.PhysicalDefense);
		var damageTakenScale = targetAsc.GetCurrentValue(AttributeIds.DamageTakenScale);
		var amountFromCaller = spec.SetByCaller.TryGetValue(SetByCallerAmount, out var amount) ? amount : 0f;
		var baseDamage = MathF.Max(0f, amountFromCaller + sourceAttack + sourceDamageBonus - targetDefense);
		var multiplier = MathF.Max(0f, 1f + damageTakenScale);
		var damage = MathF.Max(0f, baseDamage * multiplier);
		if (damage <= 0f)
			return;

		var currentHealth = targetAsc.GetCurrentValue(AttributeIds.Health);
		targetAsc.Attributes.SetCurrentValue(AttributeIds.Health, MathF.Max(0f, currentHealth - damage));
	}
}
