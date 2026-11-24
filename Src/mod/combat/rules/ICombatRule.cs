namespace KemoCard.Mod.Combat.Rules;

public interface ICombatRule
{
    string Id { get; }
    int Priority { get; }

    void OnBattleStart(CombatContext ctx) { }
    void OnTurnStart(CombatContext ctx) { }
    void OnTurnEnd(CombatContext ctx) { }
    /// <summary>修改本地伤害包的数额。预演与提交共用，不能改动战斗或规则实例状态。
    /// <b>所有影响伤害数额的修减必须实现在这里</b>：无副作用预演（弹珠伤害预览）只运行本钩子，
    /// 不运行 <see cref="OnBeforeDamage"/>。</summary>
    void ModifyDamage(CombatContext ctx, ref DamagePacket packet) { }
    /// <summary>
    /// 仅在实际受击 / 提交伤害时触发，无副作用预演不会调用。
    /// 需要让预演与结算保持一致的减伤 / 增伤请实现 <see cref="ModifyDamage"/>；
    /// 本钩子只用于"确实命中后"的副作用（计数、条件触发等）。
    /// </summary>
    void OnBeforeDamage(CombatContext ctx, ref DamagePacket packet) { }
    void OnAfterDamage(CombatContext ctx, in DamagePacket packet) { }
    void OnCheckEndCondition(CombatContext ctx, ref EndDecision decision) { }
}