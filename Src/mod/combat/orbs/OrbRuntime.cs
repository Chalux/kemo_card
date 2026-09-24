using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Orbs;

/// <summary>一次充能球触发的结果（命令返回、调试输出与测试断言共用）。</summary>
public sealed record OrbTriggerResult(
    bool Triggered,
    IReadOnlyDictionary<string, int> ClearedByType,
    string? Error = null)
{
    public static OrbTriggerResult NotTriggered(string error) =>
        new(false, new Dictionary<string, int>(StringComparer.Ordinal), error);
}

/// <summary>
/// 充能球（元素球）运行时：全队共享队列的授予、触发与回合结束产出。
/// </summary>
/// <remarks>
/// <para><b>触发</b>：出牌阶段球数 ≥ <see cref="OrbQueue.ManualTriggerThreshold"/> 可主动触发；球数达到
/// <see cref="OrbQueue.Capacity"/> 时"获得即触发"（自动）。触发一次清空队列，按入队顺序（FIFO）
/// <b>逐球</b>结算：每个球跑一次自身类型的效果，源为该球的产球者。</para>
/// <para><b>单球伤害</b> = <c>perOrbAmount + attackBonusScale × 产球者攻击</c>
/// （攻击按球类型的 attackSource 取物攻/魔攻/两者较高者）
/// × (1 + 产球者增伤 + 目标受伤增加)（增伤与受伤增加同桶加算，含 <c>OrbDamageScale</c> 球伤害增加）；
/// <b>不吃目标物防/魔防、不吃连携</b>。</para>
/// <para><b>回合结束产出</b>：固定 1 个四属性球 + 1 个物理/魔法球。四属性球按"本回合打出的卡牌自身
/// element"计数取最多者，物理/魔法球按卡牌类型（Physics/Magical）计数取最多者；平局或本回合未出牌
/// 时随机（走 <c>combat.orb</c> 独立随机流）。产球者：四属性球 = 全队 max(物攻,魔攻) 最高者、
/// 物理球 = 物攻最高者、魔法球 = 魔攻最高者（当前有效值，并列取槽序最小）。</para>
/// </remarks>
public sealed class OrbRuntime
{
    private readonly GameDefinitionRegistry _registry;

    public OrbRuntime(GameDefinitionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>全队共享的充能球队列。</summary>
    public OrbQueue Queue { get; } = new();

    #region 授予

    /// <summary>
    /// 授予充能球。<paramref name="producerIndex"/> 为产球者槽位（&lt;0 = 无产球者，触发时按全队最高攻击者解析）。
    /// 未知球类型是软失败（返回 false）；每授予一个球后检查满员即刻触发。
    /// </summary>
    public bool Grant(CombatSimulation simulation, string orbTypeId, int producerIndex, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (count <= 0 || string.IsNullOrWhiteSpace(orbTypeId))
            return false;
        if (!_registry.Store.TryGetOrbType(orbTypeId, out _))
            return false;

        var granted = false;
        for (var i = 0; i < count; i++)
        {
            if (!Queue.TryEnqueue(new OrbInstance(orbTypeId, producerIndex)))
                break;

            granted = true;
            simulation.Presentation.Emit(new OrbGainedEvent(orbTypeId, producerIndex, Queue.Count));
            if (Queue.IsFull)
                Trigger(simulation, automatic: true);
        }

        return granted;
    }

    /// <summary>
    /// 回合结束产出：1 个四属性球 + 1 个物理/魔法球（统计口径见类型注释）。
    /// 每次产出后按满员规则即时触发；产出顺序固定（先属性球后物理/魔法球）。
    /// </summary>
    public void GrantTurnEndOrbs(CombatSimulation simulation, IReadOnlyList<PlayedCardRecord> playedCards)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(playedCards);

        var elementCounts = new Dictionary<EElement, int>(BuiltinOrbTypes.ElementOrbs.Count);
        foreach (var (element, _) in BuiltinOrbTypes.ElementOrbs)
            elementCounts[element] = 0;
        var physicalCount = 0;
        var magicCount = 0;

        foreach (var record in playedCards)
        {
            if (!_registry.Store.TryGetCard(record.CardId, out var card))
                continue;

            foreach (var (element, _) in BuiltinOrbTypes.ElementOrbs)
            {
                if ((card.Element & (int)element) != 0)
                    elementCounts[element]++;
            }

            switch (card.CardType)
            {
                case ECardType.Physics:
                    physicalCount++;
                    break;
                case ECardType.Magical:
                    magicCount++;
                    break;
            }
        }

        var elementOrb = PickElementOrb(simulation, elementCounts);
        var attackOrb = PickAttackOrb(simulation, physicalCount, magicCount);

        if (elementOrb is not null)
        {
            var producer = ResolveHighestAttackCharacter(simulation, EOrbAttackSource.Higher);
            Grant(simulation, elementOrb, producer);
        }

        if (attackOrb is not null)
        {
            var attackSource = string.Equals(attackOrb, BuiltinOrbTypes.Physical, StringComparison.Ordinal)
                ? EOrbAttackSource.Physical
                : EOrbAttackSource.Magic;
            var producer = ResolveHighestAttackCharacter(simulation, attackSource);
            Grant(simulation, attackOrb, producer);
        }
    }

    private string? PickElementOrb(CombatSimulation simulation, Dictionary<EElement, int> counts)
    {
        var total = counts.Values.Sum();
        if (total == 0)
            return RandomPick(simulation, [.. BuiltinOrbTypes.ElementOrbs.Select(entry => entry.OrbTypeId)]);

        var best = counts.Values.Max();
        var candidates = BuiltinOrbTypes.ElementOrbs
            .Where(entry => counts[entry.Element] == best)
            .Select(entry => entry.OrbTypeId)
            .ToList();
        return RandomPick(simulation, candidates);
    }

    private string? PickAttackOrb(CombatSimulation simulation, int physicalCount, int magicCount)
    {
        if (physicalCount == 0 && magicCount == 0)
            return RandomPick(simulation, BuiltinOrbTypes.AttackOrbs);
        if (physicalCount > magicCount)
            return BuiltinOrbTypes.Physical;
        if (magicCount > physicalCount)
            return BuiltinOrbTypes.Magic;

        return RandomPick(simulation, BuiltinOrbTypes.AttackOrbs);
    }

    private static string? RandomPick(CombatSimulation simulation, IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0)
            return null;

        return candidates[simulation.OrbRng.NextInt(0, candidates.Count)];
    }

    #endregion

    #region 触发

    /// <summary>出牌阶段的主动触发：球数不足 <see cref="OrbQueue.ManualTriggerThreshold"/> 时失败且不动队列。</summary>
    public OrbTriggerResult TriggerManual(CombatSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (!Queue.CanTriggerManually)
        {
            return OrbTriggerResult.NotTriggered(
                $"充能球不足 {OrbQueue.ManualTriggerThreshold} 个（当前 {Queue.Count}）。");
        }

        return Trigger(simulation, automatic: false);
    }

    /// <summary>
    /// 清空队列并逐球结算（FIFO）。敌方无存活目标时照常清空、不产生伤害
    /// （避免满员后卡死队列）。自动触发与主动触发行为完全一致。
    /// </summary>
    public OrbTriggerResult Trigger(CombatSimulation simulation, bool automatic)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        var drained = Queue.DrainAll();
        if (drained.Count == 0)
            return OrbTriggerResult.NotTriggered("充能球队列为空。");

        // 表现事件在结算前记录：界面先播"整排闪光/清空"，其后的伤害事件按 FIFO 逐球跟随。
        simulation.Presentation.Emit(new OrbsTriggeredEvent(
            [.. drained.Select(orb => orb.OrbTypeId)],
            automatic));

        var cleared = new Dictionary<string, int>(StringComparer.Ordinal);
        var producers = new List<int>();
        var enemies = AliveEnemies(simulation);
        foreach (var orb in drained)
        {
            if (!_registry.Store.TryGetOrbType(orb.OrbTypeId, out var orbType))
                continue;

            cleared[orb.OrbTypeId] = cleared.GetValueOrDefault(orb.OrbTypeId) + 1;

            var producerIndex = orb.ProducerIndex >= 0
                ? orb.ProducerIndex
                : ResolveHighestAttackCharacter(simulation, orbType.AttackSource);
            var source = ToSourceRef(producerIndex);
            producers.Add(producerIndex);

            if (orbType.DealsDamage && enemies.Count > 0)
                ApplyOrbDamage(simulation, orbType, producerIndex, source, enemies);

            foreach (var effectRef in orbType.TriggerEffects)
            {
                // 触发效果的目标按效果参数解析（hookTargets / targetFilter），缺省为产球者自身。
                var targets = CombatTargetSelector.Resolve(simulation, source, effectRef.Params);
                simulation.EffectExecutor.ExecuteEffectRef(effectRef, simulation, source, targets);
            }
        }

        // 触发后钩子（onOrbTriggered）：按产球者去重，配合 oncePerTurn 实现"每回合仅 1 次"。
        simulation.Buffs.FireOrbTriggered(simulation, producers);
        // 回合内统计：供"本回合每触发 N 个 X 球 → 增伤"这类效果读取。
        simulation.RecordOrbsTriggered(cleared);

        return new OrbTriggerResult(true, cleared);
    }

    private static void ApplyOrbDamage(
        CombatSimulation simulation,
        OrbTypeDto orbType,
        int producerIndex,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> enemies)
    {
        var attack = ResolveAttack(simulation, producerIndex, orbType.AttackSource);
        var baseAmount = orbType.PerOrbAmount + (orbType.AttackBonusScale * attack);
        var asc = ResolvePlayerAsc(simulation, producerIndex);
        var dealtScale = asc?.GetCurrentValue(AttributeIds.DamageDealtScale) ?? 0f;
        // 球伤害增加：无掩码的那份（所有球） + 命中该球元素的那几份。
        var orbScale = asc?.GetCurrentValue(AttributeIds.OrbDamageScale) ?? 0f;
        foreach (var element in Enum.GetValues<EElement>())
        {
            if (element != EElement.None && (orbType.Element & element) != 0)
                orbScale += asc?.GetCurrentValue(AttributeIds.OrbDamageScaleFor(element)) ?? 0f;
        }

        // 球侧增伤（全伤害增加 + 球伤害增加）与目标受伤增加同桶加算（规格：一律加算），逐目标算。
        var dealtBonus = dealtScale + orbScale;
        if (baseAmount <= 0f)
            return;

        foreach (var enemy in enemies)
        {
            // 规则管线（OnBeforeDamage/OnAfterDamage）由 ApplyFixedDamage 负责。
            var perTarget = baseAmount *
                DamageScaling.CombineBonuses(dealtBonus, DamagePipeline.ResolveTakenScale(simulation, enemy));
            if (perTarget <= 0f)
                continue;

            simulation.EffectExecutor.ApplyFixedDamage(
                simulation,
                source,
                [enemy],
                perTarget,
                orbType.Id,
                orbType.DamageKind,
                orbType.Element);
        }
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

    #endregion

    #region 产球者解析

    /// <summary>
    /// 全队攻击最高者（按当前有效值；并列取槽序最小）。无人时返回 -1（触发时回落到队伍账本为源）。
    /// </summary>
    private static int ResolveHighestAttackCharacter(CombatSimulation simulation, EOrbAttackSource attackSource)
    {
        var bestIndex = -1;
        var bestValue = -1f;
        for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
        {
            var value = ResolveAttack(simulation, i, attackSource);
            if (value > bestValue)
            {
                bestValue = value;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static float ResolveAttack(CombatSimulation simulation, int producerIndex, EOrbAttackSource attackSource)
    {
        var asc = ResolvePlayerAsc(simulation, producerIndex);
        if (asc is null)
            return 0f;

        var physical = asc.GetCurrentValue(AttributeIds.PhysicalAttack);
        var magic = asc.GetCurrentValue(AttributeIds.MagicAttack);
        return attackSource switch
        {
            EOrbAttackSource.Physical => physical,
            EOrbAttackSource.Magic => magic,
            _ => MathF.Max(physical, magic),
        };
    }

    private static CombatTargetRef ToSourceRef(int producerIndex) =>
        producerIndex >= 0 ? new CombatTargetRef(ECombatSide.Player, producerIndex) : CombatTargetRef.PlayerTeam;

    private static AbilitySystemComponent? ResolvePlayerAsc(CombatSimulation simulation, int characterIndex) =>
        characterIndex >= 0 && characterIndex < simulation.PlayerTeam.Characters.Count
            ? simulation.PlayerTeam.Characters[characterIndex].Asc
            : null;

    private static AbilitySystemComponent? ResolveAsc(CombatSimulation simulation, CombatTargetRef target)
    {
        if (target.Side == ECombatSide.Enemy &&
            target.Index >= 0 && target.Index < simulation.EnemyTeam.Enemies.Count)
            return simulation.EnemyTeam.Enemies[target.Index].Asc;
        if (target.Side == ECombatSide.Player)
            return ResolvePlayerAsc(simulation, target.Index);
        return null;
    }

    #endregion
}