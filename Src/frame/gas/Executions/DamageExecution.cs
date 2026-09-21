using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Gas.Executions;

public sealed class DamageExecution : IExecutionCalculation
{
    public const string DamageKind = "Damage";
    private const string SetByCallerAmount = "Amount";

    /// <summary>连携等一次性加成通道：与源侧 DamageDealtScale 同桶加算后乘算。</summary>
    public const string SetByCallerChainBonusScale = "ChainBonusScale";

    public string Kind => DamageKind;

    public void Execute(ExecutionDefDto executionDef, GameplayEffectSpec spec, AbilitySystemComponent targetAsc)
    {
        ArgumentNullException.ThrowIfNull(executionDef);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(targetAsc);

        var sourceAttack = spec.SourceAsc?.GetCurrentValue(AttributeIds.PhysicalAttack) ?? 0f;
        var sourceDamageBonus = spec.SourceAsc?.GetCurrentValue(AttributeIds.Damage) ?? 0f;
        var sourceDealtScale = spec.SourceAsc?.GetCurrentValue(AttributeIds.DamageDealtScale) ?? 0f;
        var chainBonus = spec.SetByCaller.TryGetValue(SetByCallerChainBonusScale, out var bonus) ? bonus : 0f;
        var targetDefense = targetAsc.GetCurrentValue(AttributeIds.PhysicalDefense);
        var damageTakenScale = targetAsc.GetCurrentValue(AttributeIds.DamageTakenScale);
        var amountFromCaller = spec.SetByCaller.TryGetValue(SetByCallerAmount, out var amount) ? amount : 0f;
        var baseDamage = MathF.Max(0f, amountFromCaller + sourceAttack + sourceDamageBonus - targetDefense);
        // 全伤害增加与连携加成同桶加算（加算叠满后再与受伤倍率相乘）。
        var dealtMultiplier = MathF.Max(0f, 1f + sourceDealtScale + chainBonus);
        var multiplier = MathF.Max(0f, 1f + damageTakenScale);
        var damage = MathF.Max(0f, baseDamage * dealtMultiplier * multiplier);
        if (damage <= 0f)
            return;

        var currentHealth = targetAsc.GetCurrentValue(AttributeIds.Health);
        targetAsc.Attributes.SetCurrentValue(AttributeIds.Health, MathF.Max(0f, currentHealth - damage));
    }
}