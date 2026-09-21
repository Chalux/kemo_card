using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.NormalAttack;

/// <summary>一次普通攻击的执行结果（供战斗日志、调试面板与测试断言）。</summary>
public sealed record NormalAttackResult(
    int SlotIndex,
    string CharacterDefinitionId,
    EDamageKind Kind,
    EElement Element,
    int TargetCount,
    float TotalDamage);

/// <summary>
/// 普通攻击（规格：普通攻击）：每回合所有卡牌结算完成后自动执行一次。
/// </summary>
/// <remarks>
/// <para><b>归属槽位</b>：<c>(TurnNumber - 1) % 队伍人数</c>。第 1 回合 → 槽位 1，第 5 回合 → 槽位 1
/// （4 人队伍每 4 回合轮完一圈）。回合计数用<b>全场累计</b> <see cref="CombatSimulation.TurnNumber"/>，
/// 换波不清零。</para>
/// <para><b>类型与数值</b>：比较该角色的物攻与魔攻，较高者决定类型（平手取物理，保证确定性）；
/// 伤害 = <c>max(0, 攻击力 × NormalAttackScale − 目标对应防御)</c>（物理减物防、魔法减魔防）。</para>
/// <para><b>元素</b>：带出攻击者的<b>全部</b>元素（多元素角色全部生效），只作伤害标签，当前无克制消费方。</para>
/// <para><b>放大/减免</b>：吃攻击者的全伤害增加与目标的受伤倍率，并走统一的伤害包管线
/// （<c>OnBeforeDamage</c> 可改数额 → 写入 → <c>OnAfterDamage</c>）；<b>不吃连携</b>（连携只作用于伤害/治疗卡）。</para>
/// <para><b>解耦</b>：不算"打出牌"——不触发槽位 buff（充能/槽位伤害/onSlotCardPlayed），不计入连携人头，
/// 也不计入回合结束的充能球产出统计；不消耗能量、不占"已行动"、不可取消；被封印的角色仍会普攻。</para>
/// </remarks>
public sealed class NormalAttackRuntime
{
    /// <summary>上一次执行结果（未执行过为 null）；调试面板与 UI 用它展示。</summary>
    public NormalAttackResult? LastResult { get; private set; }

    /// <summary>本场战斗累计执行次数。</summary>
    public int ExecutionCount { get; private set; }

    /// <summary>
    /// 本回合的普攻归属槽位（0-based）；队伍为空返回 -1。
    /// </summary>
    public int ResolveSlotIndex(CombatSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        return ResolveSlotIndex(simulation.TurnNumber, simulation.PlayerTeam.Characters.Count);
    }

    /// <summary>归属槽位的纯函数口径：<c>(回合 - 1) % 队伍人数</c>（队伍为空返回 -1）。</summary>
    public static int ResolveSlotIndex(int turnNumber, int characterCount)
    {
        if (characterCount <= 0)
            return -1;

        return (((turnNumber - 1) % characterCount) + characterCount) % characterCount;
    }

    /// <summary>
    /// 执行本回合的普通攻击。无存活敌人时仍算作"已执行"（消费掉本回合的机会），但不产生伤害。
    /// </summary>
    public NormalAttackResult? Execute(CombatSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        var slotIndex = ResolveSlotIndex(simulation);
        if (slotIndex < 0)
            return null;

        var attacker = simulation.PlayerTeam.Characters[slotIndex];
        var asc = attacker.Asc;
        var physicalAttack = asc.GetCurrentValue(AttributeIds.PhysicalAttack);
        var magicAttack = asc.GetCurrentValue(AttributeIds.MagicAttack);
        var kind = magicAttack > physicalAttack ? EDamageKind.Magical : EDamageKind.Physical;
        var attack = kind == EDamageKind.Magical ? magicAttack : physicalAttack;

        var targets = AliveEnemies(simulation);
        var source = new CombatTargetRef(ECombatSide.Player, slotIndex);
        var dealtScale = MathF.Max(0f, 1f + asc.GetCurrentValue(AttributeIds.DamageDealtScale));
        var totalDamage = 0f;
        var hitCount = 0;

        foreach (var target in targets)
        {
            var defense = ResolveDefense(simulation, target, kind);
            var baseDamage = MathF.Max(0f, (attack * CombatConstants.NormalAttackScale) - defense);
            if (baseDamage <= 0f)
                continue;

            // 受伤倍率逐目标算；规则管线（OnBeforeDamage / 写入 / OnAfterDamage）由 ApplyFixedDamage 统一负责。
            var takenScale = MathF.Max(0f, 1f + DamagePipeline.ResolveTakenScale(simulation, target));
            var applied = simulation.EffectExecutor.ApplyFixedDamage(
                simulation,
                source,
                [target],
                baseDamage * dealtScale * takenScale,
                effectId: null,
                kind,
                attacker.Element);
            if (applied <= 0f)
                continue;

            totalDamage += applied;
            hitCount++;
        }

        var result = new NormalAttackResult(
            slotIndex,
            attacker.DefinitionId,
            kind,
            attacker.Element,
            hitCount,
            totalDamage);
        LastResult = result;
        ExecutionCount++;
        return result;
    }

    /// <summary>物理减物防、魔法减魔防；元素类伤害不吃防御（本机制不会产生元素类）。</summary>
    private static float ResolveDefense(CombatSimulation simulation, CombatTargetRef target, EDamageKind kind)
    {
        var asc = CombatGasBridge.ResolveTargetAsc(simulation, target);
        if (asc is null)
            return 0f;

        return kind switch
        {
            EDamageKind.Magical => asc.GetCurrentValue(AttributeIds.MagicDefense),
            EDamageKind.Elemental => 0f,
            _ => asc.GetCurrentValue(AttributeIds.PhysicalDefense),
        };
    }

    private static IReadOnlyList<CombatTargetRef> AliveEnemies(CombatSimulation simulation)
    {
        var alive = new List<CombatTargetRef>();
        for (var i = 0; i < simulation.EnemyTeam.Enemies.Count; i++)
        {
            if (simulation.EnemyTeam.Enemies[i].IsAlive)
                alive.Add(new CombatTargetRef(ECombatSide.Enemy, i));
        }

        return alive;
    }
}
