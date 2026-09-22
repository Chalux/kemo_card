using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Gas.Executions;

/// <summary>
/// GAS 公式通道的伤害执行（战斗规格「伤害包管线」§1.3）：
/// <c>base = Amount + 源攻 − 目标对应防御</c>，攻/防由 <see cref="ExecutionDefDto.DamageType"/>（+ <c>element</c>）决定——
/// <list type="bullet">
/// <item><c>Physical</c>：<c>PhysicalAttack − PhysicalDefense</c>（默认，与历史行为一致）；</item>
/// <item><c>Magical</c>：<c>MagicAttack − MagicDefense</c>（2026-09-21 落地；此前 <c>damageType</c> 未被读取，
/// 法术也按物攻物防算）；</item>
/// <item><c>Elemental</c>：不吃攻防，只结算 <c>Amount + 源 Damage</c>（元素球 / 纯属性效果）。</item>
/// </list>
/// 缩放口径（战斗规格 §1.3「增伤与受伤增加一律加算」）：
/// <c>× (1 + 源增伤 + 目标受伤增加) × (1 + 连携)</c>——增伤与受伤增加同桶加算，只有连携单独乘算。
/// 属性标签本身不改数额（供规则/连携使用），但会随伤害包广播给规则。
/// </summary>
public sealed class DamageExecution : IExecutionCalculation
{
    public const string DamageKind = "Damage";
    private const string SetByCallerAmount = "Amount";

    /// <summary>
    /// SetByCaller 里的源攻击力系数覆盖键：调用方算好动态系数（例如"本回合每触发 1 个绿球 +100% 魔攻"）
    /// 后塞进来，缺省时用 <see cref="ExecutionDefDto.AttackScale"/>。
    /// </summary>
    public const string SetByCallerAttackScale = "AttackScale";

    /// <summary>连携等一次性加成通道：与源侧 DamageDealtScale 同桶加算后乘算。</summary>
    public const string SetByCallerChainBonusScale = "ChainBonusScale";

    public string Kind => DamageKind;

    public void Execute(ExecutionDefDto executionDef, GameplayEffectSpec spec, AbilitySystemComponent targetAsc)
    {
        ArgumentNullException.ThrowIfNull(executionDef);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(targetAsc);

        var descriptor = DamageTypeParser.Parse(executionDef.DamageType, executionDef.Element);
        var (sourceAttack, targetDefense) = ResolveAttackAndDefense(descriptor.Kind, spec, targetAsc);
        // 源攻击力系数：缺省 1.0（100% 攻击力）；「3 + 25% 物攻」这类卡填 0.25。
        // 调用方也可用 SetByCaller["AttackScale"] 覆盖（内容无法静态声明的动态系数，如"按本回合触发球数提升"）。
        var attackScale = spec.SetByCaller.TryGetValue(SetByCallerAttackScale, out var scaleOverride)
            ? scaleOverride
            : executionDef.AttackScale;
        sourceAttack *= MathF.Max(0f, attackScale);

        var sourceDamageBonus = spec.SourceAsc?.GetCurrentValue(AttributeIds.Damage) ?? 0f;
        var sourceDealtScale = spec.SourceAsc?.GetCurrentValue(AttributeIds.DamageDealtScale) ?? 0f;
        var chainBonus = spec.SetByCaller.TryGetValue(SetByCallerChainBonusScale, out var bonus) ? bonus : 0f;
        var damageTakenScale = targetAsc.GetCurrentValue(AttributeIds.DamageTakenScale);
        var amountFromCaller = spec.SetByCaller.TryGetValue(SetByCallerAmount, out var amount) ? amount : 0f;
        var baseDamage = MathF.Max(0f, amountFromCaller + sourceAttack + sourceDamageBonus - targetDefense);
        // 战斗规格「增伤与受伤增加一律加算」：源侧增伤 + 目标侧受伤增加进同一个加算桶，只有连携单独乘算。
        var bonusMultiplier = MathF.Max(0f, 1f + sourceDealtScale + damageTakenScale);
        var chainMultiplier = MathF.Max(0f, 1f + chainBonus);
        var damage = MathF.Max(0f, baseDamage * bonusMultiplier * chainMultiplier);
        if (damage <= 0f)
            return;

        var currentHealth = targetAsc.GetCurrentValue(AttributeIds.Health);
        targetAsc.Attributes.SetCurrentValue(AttributeIds.Health, MathF.Max(0f, currentHealth - damage));
    }

    /// <summary>
    /// 攻防取值：物理吃物攻物防、魔法吃魔攻魔防、元素两者都不吃（规格 §1.3「Elemental 不吃物防/魔防」）。
    /// </summary>
    private static (float Attack, float Defense) ResolveAttackAndDefense(
        EDamageKind kind,
        GameplayEffectSpec spec,
        AbilitySystemComponent targetAsc) => kind switch
        {
            EDamageKind.Magical =>
                (spec.SourceAsc?.GetCurrentValue(AttributeIds.MagicAttack) ?? 0f,
                    targetAsc.GetCurrentValue(AttributeIds.MagicDefense)),
            EDamageKind.Elemental => (0f, 0f),
            _ =>
                (spec.SourceAsc?.GetCurrentValue(AttributeIds.PhysicalAttack) ?? 0f,
                    targetAsc.GetCurrentValue(AttributeIds.PhysicalDefense)),
        };
}