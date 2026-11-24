namespace KemoCard.Mod.Combat.Rules;

public sealed class CombatRuleEngine
{
    private readonly IReadOnlyList<ICombatRule> _rules;

    public IReadOnlyList<ICombatRule> Rules => _rules;

    public CombatRuleEngine(IEnumerable<ICombatRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.OrderBy(r => r.Priority).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
    }

    public void DispatchTurnStart(CombatContext ctx, Action<ICombatRule>? trace = null)
    {
        foreach (var rule in _rules)
        {
            trace?.Invoke(rule);
            rule.OnTurnStart(ctx);
        }
    }

    public void DispatchBeforeDamage(CombatContext ctx, ref DamagePacket packet)
    {
        foreach (var rule in _rules)
        {
            rule.ModifyDamage(ctx, ref packet);
            rule.OnBeforeDamage(ctx, ref packet);
        }
    }

    /// <summary>
    /// 无副作用预演：只运行 <see cref="ICombatRule.ModifyDamage"/>。
    /// 影响伤害数额的规则必须实现在该钩子；<see cref="ICombatRule.OnBeforeDamage"/> 属于实际受击副作用，
    /// 预演不得触发（否则会写入战斗状态）。
    /// </summary>
    internal void PreviewDamage(CombatContext ctx, ref DamagePacket packet)
    {
        foreach (var rule in _rules)
            rule.ModifyDamage(ctx, ref packet);
    }

    /// <summary>伤害写入完成后广播（<paramref name="packet"/> 为只读的最终数额，供观测/记日志类规则使用）。</summary>
    public void DispatchAfterDamage(CombatContext ctx, in DamagePacket packet)
    {
        foreach (var rule in _rules)
            rule.OnAfterDamage(ctx, in packet);
    }

    public void DispatchTurnEnd(CombatContext ctx)
    {
        foreach (var rule in _rules)
            rule.OnTurnEnd(ctx);
    }

    public void DispatchCheckEndCondition(CombatContext ctx, ref EndDecision decision)
    {
        foreach (var rule in _rules)
            rule.OnCheckEndCondition(ctx, ref decision);
    }
}