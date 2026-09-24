using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.NormalAttack;

/// <summary>单次打击（普攻或追打的一次攻击）：同一次打击对全体存活敌人各结算一份。</summary>
public sealed record NormalAttackStrike(
    int SlotIndex,
    string CharacterDefinitionId,
    float Percent,
    EDamageKind Kind,
    EElement Element,
    int TargetCount,
    float Damage)
{
    /// <summary>是否为普攻归属角色本人的打击（false = 追打）。</summary>
    public bool IsOwnerStrike { get; init; }
}

/// <summary>一次普通攻击的执行结果（供战斗日志、调试面板与测试断言）。</summary>
/// <remarks>
/// 2026-09-21 起一次"普攻"可能包含多轮：归属角色的 <c>NormalAttackCount</c> 额外次数，
/// 以及每次归属打击之后按槽位顺序补打的追打。因此新增 <see cref="Strikes"/>（逐次明细）与
/// <see cref="Executions"/>（归属角色的打击轮数）；<see cref="SlotIndex"/> / <see cref="Kind"/> /
/// <see cref="Element"/> / <see cref="TargetCount"/> 保持"归属角色第一次打击"的口径不变，
/// <see cref="TotalDamage"/> 升级为<b>本回合普攻总输出</b>（含追打与多轮）。
/// </remarks>
public sealed record NormalAttackResult(
    int SlotIndex,
    string CharacterDefinitionId,
    EDamageKind Kind,
    EElement Element,
    int TargetCount,
    float TotalDamage,
    IReadOnlyList<NormalAttackStrike> Strikes,
    int Executions)
{
    /// <summary>参与本回合普攻的角色数（归属 + 追打者，去重）。</summary>
    public int ParticipantCount => Strikes.Select(strike => strike.SlotIndex).Distinct().Count();

    /// <summary>本回合追打贡献的总伤害。</summary>
    public float FollowUpDamage => Strikes.Where(strike => !strike.IsOwnerStrike).Sum(strike => strike.Damage);
}

/// <summary>
/// 普通攻击（规格：普通攻击）：每回合所有卡牌结算完成后自动执行。
/// </summary>
/// <remarks>
/// <para><b>归属槽位</b>：<c>(TurnNumber - 1) % 队伍人数</c>。第 1 回合 → 槽位 1，第 5 回合 → 槽位 1
/// （4 人队伍每 4 回合轮完一圈）。回合计数用<b>全场累计</b> <see cref="CombatSimulation.TurnNumber"/>，
/// 换波不清零。</para>
/// <para><b>次数</b>：归属角色的 <c>NormalAttackCount</c> 提供<b>额外</b>轮数，总计
/// <c>1 + max(0, 该属性)</c> 轮；每轮都是"归属者先打 → 追打者按槽位顺序补打"。</para>
/// <para><b>类型与数值</b>：比较该角色的物攻与魔攻，较高者决定类型（平手取物理，保证确定性）；
/// 伤害 = <c>max(0, 攻击力 × 系数 × NormalAttackScale − 目标对应防御)</c>（物理减物防、魔法减魔防）。
/// 追打的"系数"来自 <see cref="FollowUpAttack"/>（百分比），归属打击为 1。</para>
/// <para><b>元素</b>：带出攻击者的<b>全部</b>元素（多元素角色全部生效），只作伤害标签，当前无克制消费方。</para>
/// <para><b>放大/减免</b>：吃攻击者的全伤害增加、目标的 <c>DamageTakenScale</c> 与
/// <c>NormalAttackDamageTakenScale</c>（只对普攻生效的受伤倍率），并走统一的伤害包管线
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

        var characters = simulation.PlayerTeam.Characters;
        var owner = characters[slotIndex];
        var executions = 1 + (int)MathF.Max(0f, owner.Asc.GetCurrentValue(AttributeIds.NormalAttackCount));
        var strikes = new List<NormalAttackStrike>();

        for (var round = 0; round < executions; round++)
        {
            Strike(simulation, slotIndex, percent: 1f, isOwner: true, strikes);
            foreach (var (index, percent) in FollowUpAttack.ResolveParticipants(characters, slotIndex))
                Strike(simulation, index, percent / 100f, isOwner: false, strikes);
        }

        var ownerStrike = strikes.FirstOrDefault(strike => strike.IsOwnerStrike);
        var result = new NormalAttackResult(
            slotIndex,
            owner.DefinitionId,
            ownerStrike?.Kind ?? EDamageKind.Physical,
            ownerStrike?.Element ?? owner.Element,
            ownerStrike?.TargetCount ?? 0,
            strikes.Sum(strike => strike.Damage),
            strikes,
            executions);
        LastResult = result;
        ExecutionCount++;
        return result;
    }

    /// <summary>一次打击：对每个存活敌人各结算一份（逐目标算受伤倍率）。</summary>
    private static void Strike(
        CombatSimulation simulation,
        int characterIndex,
        float percent,
        bool isOwner,
        List<NormalAttackStrike> strikes)
    {
        var attacker = simulation.PlayerTeam.Characters[characterIndex];
        var asc = attacker.Asc;
        var physicalAttack = asc.GetCurrentValue(AttributeIds.PhysicalAttack);
        var magicAttack = asc.GetCurrentValue(AttributeIds.MagicAttack);
        var kind = magicAttack > physicalAttack ? EDamageKind.Magical : EDamageKind.Physical;
        var attack = kind == EDamageKind.Magical ? magicAttack : physicalAttack;
        var source = new CombatTargetRef(ECombatSide.Player, characterIndex);
        // 普攻专属增伤与全伤害增加同桶（卡牌伤害不吃前者）；受伤增加在下面与它加算（规格：一律加算）。
        var dealtBonus =
            asc.GetCurrentValue(AttributeIds.DamageDealtScale) +
            asc.GetCurrentValue(AttributeIds.NormalAttackDamageDealtScale);
        var damage = 0f;
        var hitCount = 0;
        var targets = AliveEnemies(simulation);
        // 表现事件先于伤害记录：界面先播"前冲"，随后的 DamageDealt 事件逐目标跟随。
        simulation.Presentation.Emit(new NormalAttackStrikeEvent(characterIndex, kind, attacker.Element, isOwner, targets));

        foreach (var target in targets)
        {
            var defense = ResolveDefense(simulation, target, kind);
            var baseDamage = MathF.Max(0f, (attack * percent * CombatConstants.NormalAttackScale) - defense);
            if (baseDamage <= 0f)
                continue;

            var multiplier = DamageScaling.CombineBonuses(dealtBonus, ResolveTakenScale(simulation, target));
            var applied = simulation.EffectExecutor.ApplyFixedDamage(
                simulation,
                source,
                [target],
                baseDamage * multiplier,
                effectId: null,
                kind,
                attacker.Element);
            if (applied <= 0f)
                continue;

            damage += applied;
            hitCount++;
        }

        strikes.Add(new NormalAttackStrike(
            characterIndex,
            attacker.DefinitionId,
            percent * 100f,
            kind,
            attacker.Element,
            hitCount,
            damage)
        {
            IsOwnerStrike = isOwner,
        });
    }

    /// <summary>
    /// 目标受伤倍率（普攻口径）：<c>DamageTakenScale + NormalAttackDamageTakenScale</c> 同桶加算，
    /// 与 <c>DamagePipeline.ResolveTakenScale</c> 的队伍账本回落规则保持一致。
    /// </summary>
    private static float ResolveTakenScale(CombatSimulation simulation, CombatTargetRef target)
    {
        if (SharedHpSettlement.IsPlayerTeamLedger(target))
        {
            var teamAsc = simulation.PlayerTeam.Asc;
            return teamAsc.GetCurrentValue(AttributeIds.DamageTakenScale) +
                teamAsc.GetCurrentValue(AttributeIds.NormalAttackDamageTakenScale);
        }

        var asc = CombatGasBridge.ResolveTargetAsc(simulation, target);
        return asc is null
            ? 0f
            : asc.GetCurrentValue(AttributeIds.DamageTakenScale) +
                asc.GetCurrentValue(AttributeIds.NormalAttackDamageTakenScale);
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