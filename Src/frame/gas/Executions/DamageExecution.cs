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
/// <para>
/// 攻击次数（2026-09-27 新增）：调用方把 <c>params.AttackCount</c> / <c>params.AttackCountByChain</c> /
/// <c>params.AttackCountMinusDiscard</c> 解析成总次数后经 <see cref="SetByCallerAttackCount"/> 传入，
/// 本次执行就<b>逐次</b>结算 N 次伤害（缺省 1 = 历史行为）。次数对"每个目标"分别生效，
/// 每次都是一次独立的伤害事件（逐次过规则 / 护盾 / 受击钩子，表现层也逐次记账）；
/// 每次的数额完全相同——<c>Amount</c> / <c>AttackScale</c> 不随后续命中变化。
/// </para>
/// </summary>
public sealed class DamageExecution : IExecutionCalculation
{
    public const string DamageKind = "Damage";
    private const string SetByCallerAmount = "Amount";

    /// <summary>
    /// SetByCaller 里的<b>攻击次数</b>键：调用方（<c>DamageScaling.ResolveAttackCount</c>）把动态次数
    /// 算好后塞进来。缺省 1 = 每次执行只结算一次（历史行为）；<c>0</c> = 本次执行不造成伤害。
    /// </summary>
    public const string SetByCallerAttackCount = "AttackCount";

    /// <summary>
    /// 单次执行的次数上限：防御性护栏——次数来自内容参数，写错一个数量级（如多打两个 0）就会把结算循环拉爆
    /// （与 <c>MaxChainDepth</c> 同口径的兜底）。正常内容（2~4 段）离它极远，不替代内容准入校验。
    /// </summary>
    public const int MaxAttackCount = 999;

    /// <summary>两个伤害通道共用的次数边界；先夹紧浮点值，避免整数转换溢出。</summary>
    public static int ClampAttackCount(float value) =>
        float.IsFinite(value) ? (int)MathF.Round(Math.Clamp(value, 0f, MaxAttackCount)) : 1;

    /// <summary>
    /// SetByCaller 里的源攻击力系数覆盖键：调用方算好动态系数（例如"本回合每触发 1 个绿球 +100% 魔攻"）
    /// 后塞进来，缺省时用 <see cref="ExecutionDefDto.AttackScale"/>。
    /// </summary>
    public const string SetByCallerAttackScale = "AttackScale";

    /// <summary>连携一次性加成通道：独立乘算。</summary>
    public const string SetByCallerChainBonusScale = "ChainBonusScale";

    /// <summary>由宿主卡牌上下文提供的卡牌专属增伤；区间外为 0。</summary>
    public const string SetByCallerCardDamageBonus = "CardDamageBonus";

    public string Kind => DamageKind;

    public void Execute(ExecutionDefDto executionDef, GameplayEffectSpec spec, AbilitySystemComponent targetAsc)
    {
        ArgumentNullException.ThrowIfNull(executionDef);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(targetAsc);

        var descriptor = DamageTypeParser.Parse(executionDef.DamageType, executionDef.Element);
        var (sourceAttack, targetDefense) = ResolveAttackAndDefense(descriptor.Kind, spec, targetAsc);
        // 攻击力来源覆盖（2026-09-24）：内容可点名别的属性当"攻击力"（如治疗强度）；
        // 防御侧仍按 damageType 取（魔法 → 魔防）。
        if (!string.IsNullOrWhiteSpace(executionDef.AttackAttribute))
            sourceAttack = spec.SourceAsc?.GetCurrentValue(executionDef.AttackAttribute) ?? 0f;

        // 源攻击力系数：缺省 1.0（100% 攻击力）；「3 + 25% 物攻」这类卡填 0.25。
        // 调用方也可用 SetByCaller["AttackScale"] 覆盖（内容无法静态声明的动态系数，如"按本回合触发球数提升"）。
        var attackScale = spec.SetByCaller.TryGetValue(SetByCallerAttackScale, out var scaleOverride)
            ? scaleOverride
            : executionDef.AttackScale;
        sourceAttack *= MathF.Max(0f, attackScale);

        var sourceDamageBonus = spec.SourceAsc?.GetCurrentValue(AttributeIds.Damage) ?? 0f;
        var sourceDealtScale = spec.SourceAsc?.GetCurrentValue(AttributeIds.DamageDealtScale) ?? 0f;
        if (spec.SetByCaller.TryGetValue(SetByCallerCardDamageBonus, out var cardBonus))
            sourceDealtScale += cardBonus;
        var chainBonus = spec.SetByCaller.TryGetValue(SetByCallerChainBonusScale, out var bonus) ? bonus : 0f;
        var damageTakenScale = targetAsc.GetCurrentValue(AttributeIds.DamageTakenScale);
        // 魔法专精受伤倍率（2026-09-26）：只在本通道结算魔法伤害时并入同一加算桶——
        // 物理 / 元素伤害的受伤倍率口径不变（见战斗规格「增伤与受伤增加一律加算」）。
        if (descriptor.Kind == EDamageKind.Magical)
            damageTakenScale += targetAsc.GetCurrentValue(AttributeIds.MagicDamageTakenScale);
        var amountFromCaller = spec.SetByCaller.TryGetValue(SetByCallerAmount, out var amount) ? amount : 0f;
        var baseDamage = MathF.Max(0f, amountFromCaller + sourceAttack + sourceDamageBonus - targetDefense);
        // 战斗规格「增伤与受伤增加一律加算」：源侧增伤 + 目标侧受伤增加进同一个加算桶，只有连携单独乘算。
        var bonusMultiplier = MathF.Max(0f, 1f + sourceDealtScale + damageTakenScale);
        var chainMultiplier = MathF.Max(0f, 1f + chainBonus);
        var damage = MathF.Max(0f, baseDamage * bonusMultiplier * chainMultiplier);
        var attackCount = ResolveAttackCount(spec);
        if (attackCount <= 0)
            return;

        // 「打 N 次」= 逐次结算，而不是把数额乘 N：每次都是一次独立的伤害事件，
        // 逐次过规则/护盾/受击钩子（数额本身逐次相同）。
        if (spec.DamageReceiver is not null)
        {
            for (var hit = 0; hit < attackCount; hit++)
                spec.DamageReceiver(executionDef, damage);

            return;
        }

        if (damage <= 0f)
            return;

        for (var hit = 0; hit < attackCount; hit++)
        {
            var currentHealth = targetAsc.GetCurrentValue(AttributeIds.Health);
            targetAsc.Attributes.SetCurrentValue(AttributeIds.Health, MathF.Max(0f, currentHealth - damage));
        }
    }

    /// <summary>
    /// 从 SetByCaller 取本次执行的攻击次数：缺省 1；值四舍五入到整数后夹在 <c>[0, MaxAttackCount]</c>
    /// （<c>0</c> = 本次执行不造成伤害）。
    /// </summary>
    private static int ResolveAttackCount(GameplayEffectSpec spec)
    {
        if (!spec.SetByCaller.TryGetValue(SetByCallerAttackCount, out var raw) || !float.IsFinite(raw))
            return 1;

        return ClampAttackCount(raw);
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