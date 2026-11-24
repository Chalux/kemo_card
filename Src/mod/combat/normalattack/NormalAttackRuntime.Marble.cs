using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.NormalAttack;

/// <summary>弹珠普攻分配结束后剩余 <c>Count</c> 的原因（供未来超杀机制与诊断使用）。</summary>
public enum MarbleRemainderReason
{
    /// <summary>没有剩余：全部 <c>Count</c> 都已分配。</summary>
    None = 0,

    /// <summary>本轮点数为零：跳过伤害与全部命中触发。</summary>
    ZeroCount,

    /// <summary>场上已经不存在存活敌人。</summary>
    AllEnemiesDefeated,

    /// <summary>仍有存活敌人，但全部处于换条保护或完全免疫，无法分配。</summary>
    AllTargetsProtected,

    /// <summary>可分配的伤害目标已全部处理，但仍有 <c>Count</c> 未分配。</summary>
    NoAvailableTarget,

    /// <summary>队伍为空，无法确定轮值角色。</summary>
    NoOwner,
}

/// <summary>
/// 单个目标的弹珠普攻结果：一个目标只产生<b>一包</b>基础普攻伤害，
/// 追打按追打者各追加一包。数值均为结算后的实际值；预览时字段是同一管线的预演值。
/// </summary>
/// <param name="EnemyIndex">目标敌人槽位。</param>
/// <param name="AssignedCount">分配给该目标的整数 <c>Count</c>（0 = 未分配）。</param>
/// <param name="IsMarked">是否为玩家标记目标。</param>
/// <param name="Damage">基础普攻包过规则与护盾后的伤害（不含追打；= 实际损血 + 溢出）。</param>
/// <param name="ShieldAbsorbed">基础普攻包被目标护盾吸收的数额。</param>
/// <param name="HealthLoss">基础普攻包造成的实际生命损失（不超过当前血条）。</param>
/// <param name="Overflow">基础普攻包超出当前血条 / 护盾的溢出伤害。</param>
/// <param name="FollowUpDamage">追打对该目标造成的实际生命损失合计。</param>
/// <param name="RemainingHealth">结算后当前血条的剩余生命（不会为负）。</param>
/// <param name="DepletedLifeBar">当前血条是否被打空（普通敌人 = 死亡，Boss = 换条保护）。</param>
/// <param name="EntersLifeBarProtection">血条打空且存在下一条时，是否会进入换条保护。</param>
public sealed record MarbleAttackAllocation(
    int EnemyIndex,
    int AssignedCount,
    bool IsMarked,
    float Damage,
    float ShieldAbsorbed,
    float HealthLoss,
    float Overflow,
    float FollowUpDamage,
    float RemainingHealth,
    bool DepletedLifeBar,
    bool EntersLifeBarProtection);

/// <summary>
/// 一次弹珠阶段普攻结算的完整记录：目标分配、实际损血、追打、溢出与剩余 <c>Count</c>。
/// 预览与实弹共用同一套分配算法，字段语义一致。
/// </summary>
public sealed record MarbleAttackSettlement(
    int SlotIndex,
    string CharacterDefinitionId,
    EDamageKind Kind,
    EElement Element,
    int TotalCount,
    int AllocatedCount,
    int RemainingCount,
    MarbleRemainderReason RemainingReason,
    IReadOnlyList<MarbleAttackAllocation> Allocations)
{
    /// <summary>基础普攻包伤害合计（过规则与护盾后，含溢出；不含追打）。</summary>
    public float TotalDamage => Allocations.Sum(allocation => allocation.Damage);

    /// <summary>实际生命损失合计（基础普攻 + 追打，不含护盾吸收与溢出）。</summary>
    public float TotalHealthLoss =>
        Allocations.Sum(allocation => allocation.HealthLoss + allocation.FollowUpDamage);

    /// <summary>基础普攻包溢出伤害合计（供超杀机制使用）。</summary>
    public float TotalOverflow => Allocations.Sum(allocation => allocation.Overflow);

    /// <summary>基础普攻包被护盾吸收的数额合计。</summary>
    public float TotalShieldAbsorbed => Allocations.Sum(allocation => allocation.ShieldAbsorbed);

    /// <summary>追打造成的实际生命损失合计。</summary>
    public float TotalFollowUpDamage => Allocations.Sum(allocation => allocation.FollowUpDamage);
}

/// <summary>
/// 弹珠阶段的轮值普攻结算（2026-11-23 设计）：用本轮累计 <c>Count</c> 替换旧的自动全体普攻。
/// </summary>
/// <remarks>
/// <para><b>分配</b>：标记目标优先；足以击杀时只分配所需的最小整数 <c>Count</c>（Boss 以当前血条为止），
/// 不足以击杀时拿走全部剩余 <c>Count</c>。其余目标优先选择"可击杀且所需 <c>Count</c> 最少"者，
/// 都无法击杀时选择剩余 <c>Count</c> 能造成最多实际扣血者；平手按敌方站位（索引）升序。</para>
/// <para><b>伤害</b>：每个目标只合并为一包基础普攻，公式继承旧普攻
/// <c>max(0, Count × 攻击力 × NormalAttackScale − 对应防御) × (1 + 增伤 + 受伤增加)</c>，
/// 再走统一 <see cref="DamagePipeline"/>（规则修正 → 护盾抵扣 → 生命写入）。
/// 目标护盾按 1:1 抵扣本次弹珠普攻，并计入"击杀所需 Count"。</para>
/// <para><b>追打</b>：基础普攻全部结算完后，非轮值且持有追打的队友各自对"分到 Count 且仍存活、
/// 未处于换条免疫"的目标补打一包；追打使用追打者自己的物/魔判定、元素、防御口径与增伤。</para>
/// <para><b>转盾</b>：<c>NormalAttackShieldGainScale</c> 按每个出手者本次实际造成的敌方生命损失
/// （不含护盾吸收与溢出）转化护盾，与旧普攻口径一致。</para>
/// <para><b>无副作用预览</b>：<see cref="PreviewMarbles"/> 与 <see cref="CanClearWithMarbles"/> 复用
/// <see cref="BuildPlan"/>，只做数值预演（规则回调只修改本地伤害包），不写生命 / 护盾 / 表现事件。</para>
/// </remarks>
public sealed partial class NormalAttackRuntime
{
    /// <summary>浮点生命 / 伤害比较容差（生命条打空判定）。</summary>

    /// <summary>上一次 <see cref="SettleMarbles"/> 的实际结算记录（预览不写这里）。</summary>
    public MarbleAttackSettlement? LastMarbleSettlement { get; private set; }

    /// <summary>
    /// 无副作用的伤害分配预演：与 <see cref="SettleMarbles"/> 共用同一套标记、嘲讽、最少击杀、
    /// 站位平手、护盾、免疫与追打口径，但不写生命 / 护盾、不发表现事件、不消耗效果预算。
    /// </summary>
    public MarbleAttackSettlement PreviewMarbles(CombatSimulation simulation, int count, int markedEnemyIndex)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        return ToSettlement(BuildPlan(simulation, count, markedEnemyIndex));
    }

    /// <summary>
    /// 当前 <c>Count</c> 是否已足够清场：全部普通敌人都会死亡，且每个 Boss 都会死亡、
    /// 已受换条保护或将在本次结算中触发换条保护。只做无副作用预演。
    /// </summary>
    public bool CanClearWithMarbles(CombatSimulation simulation, int count, int markedEnemyIndex)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var plan = BuildPlan(simulation, count, markedEnemyIndex);
        if (plan.SlotIndex < 0)
            return false;

        for (var index = 0; index < simulation.EnemyTeam.Enemies.Count; index++)
        {
            var enemy = simulation.EnemyTeam.Enemies[index];
            if (!enemy.IsAlive || enemy.IsAwaitingLifeBar)
                continue;

            // 完全免疫且不在换条保护中的目标不可能被本次伤害清掉。
            if (enemy.Buffs.HasTag(BuiltinBuffTags.TraitImmuneDamage))
                return false;

            if (!plan.TargetStates.TryGetValue(index, out var state) ||
                !state.DepletedLifeBar)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 以本轮累计 <c>Count</c> 结算轮值普攻：分配、基础普攻一包 / 目标、追打与转盾。
    /// <c>count</c> 为零时完全空过（不写伤害、不发命中事件）；不调用旧的全自动普攻。
    /// </summary>
    public void SettleMarbles(CombatSimulation simulation, int count, int markedEnemyIndex)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        var plan = BuildPlan(simulation, count, markedEnemyIndex);
        if (plan.SlotIndex < 0 || plan.Attacks.Count == 0)
        {
            LastMarbleSettlement = ToSettlement(plan);
            return;
        }

        var owner = simulation.PlayerTeam.Characters[plan.SlotIndex];
        var source = new CombatTargetRef(ECombatSide.Player, plan.SlotIndex);
        var settled = new List<SettledAttack>(plan.Attacks.Count);

        // 基础普攻：全部目标先各结一包（表现事件先于伤害事件）。
        simulation.Presentation.Emit(new NormalAttackStrikeEvent(
            plan.SlotIndex,
            plan.Kind,
            plan.Element,
            true,
            plan.Attacks.Select(attack => new CombatTargetRef(ECombatSide.Enemy, attack.EnemyIndex)).ToArray()));

        var ownerHealthLoss = 0f;
        foreach (var attack in plan.Attacks)
        {
            var enemy = simulation.EnemyTeam.Enemies[attack.EnemyIndex];
            var target = new CombatTargetRef(ECombatSide.Enemy, attack.EnemyIndex);
            var healthBefore = enemy.CurrentHpExact;
            var rawDamage = ComputeRawStrikeDamage(simulation, plan.OwnerProfile, attack.Count, target);
            var result = rawDamage > 0f
                ? DamagePipeline.Settle(
                    simulation,
                    source,
                    target,
                    rawDamage,
                    null,
                    plan.OwnerProfile.Kind,
                    plan.OwnerProfile.Element,
                    absorbTargetShield: true)
                : default(DamageSettlementResult);

            settled.Add(new SettledAttack
            {
                EnemyIndex = attack.EnemyIndex,
                Count = attack.Count,
                IsMarked = attack.IsMarked,
                HealthBefore = healthBefore,
                HadNextLifeBar = enemy.HasNextLifeBar,
                Base = result,
                Depleted = result.HealthLoss >= healthBefore,
            });
            ownerHealthLoss += result.HealthLoss;
        }

        ApplyShieldGain(owner, ownerHealthLoss);

        // 追打：基础普攻全部结算后，按槽位顺序逐个追打者补打；已死 / 换条免疫 / 已分配血条打空的目标跳过。
        foreach (var (followerIndex, percent) in plan.Followers)
        {
            var follower = simulation.PlayerTeam.Characters[followerIndex];
            var profile = BuildStrikeProfile(simulation, followerIndex, isOwner: false, percent / 100f);
            var eligible = settled
                .Where(item => !item.Depleted &&
                    simulation.EnemyTeam.Enemies[item.EnemyIndex].IsAlive &&
                    !simulation.EnemyTeam.Enemies[item.EnemyIndex].IsAwaitingLifeBar)
                .ToList();
            if (eligible.Count == 0)
                continue;

            simulation.Presentation.Emit(new NormalAttackStrikeEvent(
                followerIndex,
                profile.Kind,
                profile.Element,
                false,
                eligible.Select(item => new CombatTargetRef(ECombatSide.Enemy, item.EnemyIndex)).ToArray()));

            var followerHealthLoss = 0f;
            foreach (var item in eligible)
            {
                var target = new CombatTargetRef(ECombatSide.Enemy, item.EnemyIndex);
                var rawDamage = ComputeRawStrikeDamage(simulation, profile, item.Count, target);
                var result = rawDamage > 0f
                    ? DamagePipeline.Settle(
                        simulation,
                        new CombatTargetRef(ECombatSide.Player, followerIndex),
                        target,
                        rawDamage,
                        null,
                        profile.Kind,
                        profile.Element,
                        absorbTargetShield: true)
                    : default(DamageSettlementResult);

                item.FollowUpDamage += result.HealthLoss;
                item.Depleted =
                    item.Base.HealthLoss + item.FollowUpDamage >= item.HealthBefore;
                followerHealthLoss += result.HealthLoss;
            }

            ApplyShieldGain(follower, followerHealthLoss);
        }

        LastMarbleSettlement = ToSettlement(plan, settled);
    }

    #region 纯预演分配

    private static MarblePlan BuildPlan(CombatSimulation simulation, int count, int markedEnemyIndex)
    {
        var plan = new MarblePlan { TotalCount = Math.Max(0, count) };
        var slotIndex = ResolveSlotIndex(
            simulation.TurnNumber,
            simulation.PlayerTeam.Characters.Count);
        if (slotIndex >= 0)
        {
            var owner = simulation.PlayerTeam.Characters[slotIndex];
            plan.SlotIndex = slotIndex;
            plan.CharacterDefinitionId = owner.DefinitionId;
            plan.Kind = ResolveKind(owner);
            plan.Element = owner.Element;
            plan.OwnerProfile = BuildStrikeProfile(simulation, slotIndex, isOwner: true, percent: 1f);
            plan.Followers = FollowUpAttack.ResolveParticipants(simulation.PlayerTeam.Characters, slotIndex).ToArray();
        }

        if (plan.TotalCount <= 0)
        {
            plan.RemainingReason = MarbleRemainderReason.ZeroCount;
            return plan;
        }

        if (slotIndex < 0)
        {
            plan.RemainingCount = plan.TotalCount;
            plan.RemainingReason = MarbleRemainderReason.NoOwner;
            return plan;
        }

        var candidates = ResolveDamageableEnemies(simulation);
        foreach (var index in candidates)
            plan.TargetStates[index] = CreateTargetState(simulation, index);

        if (candidates.Count == 0)
        {
            plan.RemainingCount = plan.TotalCount;
            plan.RemainingReason = simulation.EnemyTeam.Enemies.Any(enemy => enemy.IsAlive)
                ? MarbleRemainderReason.AllTargetsProtected
                : MarbleRemainderReason.AllEnemiesDefeated;
            return plan;
        }

        var legalMarks = ResolveLegalMarks(simulation, candidates);
        var mark = legalMarks.Contains(markedEnemyIndex) ? markedEnemyIndex : -1;
        var allocated = new HashSet<int>();
        var remaining = plan.TotalCount;

        // 1. 标记目标优先：够击杀只拿最小整数 Count；不够则拿走全部剩余。
        if (mark >= 0)
        {
            var killCount = FindMinimumKillCount(simulation, plan.OwnerProfile, plan.TargetStates[mark], remaining);
            var assigned = killCount ?? remaining;
            plan.Attacks.Add(new PlannedAttack(mark, assigned, true));
            allocated.Add(mark);
            remaining -= assigned;
        }

        // 2. 其余目标：可击杀且所需 Count 最少者优先；都无法击杀时给剩余 Count 预计扣血最多者。
        while (remaining > 0)
        {
            var killIndex = -1;
            var killCost = int.MaxValue;
            foreach (var index in candidates)
            {
                if (allocated.Contains(index))
                    continue;
                var cost = FindMinimumKillCount(
                    simulation, plan.OwnerProfile, plan.TargetStates[index], remaining);
                if (cost is not { } value || value >= killCost)
                    continue;
                killIndex = index;
                killCost = value;
            }

            if (killIndex >= 0)
            {
                plan.Attacks.Add(new PlannedAttack(killIndex, killCost, false));
                allocated.Add(killIndex);
                remaining -= killCost;
                continue;
            }

            var fallbackIndex = -1;
            var bestHealthLoss = -1f;
            foreach (var index in candidates)
            {
                if (allocated.Contains(index))
                    continue;
                var state = plan.TargetStates[index];
                var healthLoss = ForecastBaseHealthLoss(
                    simulation, plan.OwnerProfile, state, remaining);
                if (healthLoss > bestHealthLoss)
                {
                    bestHealthLoss = healthLoss;
                    fallbackIndex = index;
                }
            }

            if (fallbackIndex < 0)
                break;

            plan.Attacks.Add(new PlannedAttack(fallbackIndex, remaining, false));
            allocated.Add(fallbackIndex);
            remaining = 0;
        }

        plan.RemainingCount = remaining;
        ForecastPlan(simulation, plan);
        plan.RemainingReason = remaining > 0
            ? DetermineRemainingReason(simulation, plan)
            : MarbleRemainderReason.None;
        return plan;
    }

    /// <summary>剩余 Count 的原因：已分配目标是否全部死亡 / 进入保护，或仍有无法分配的目标。</summary>
    private static MarbleRemainderReason DetermineRemainingReason(CombatSimulation simulation, MarblePlan plan)
    {
        var anyProtected = false;
        for (var index = 0; index < simulation.EnemyTeam.Enemies.Count; index++)
        {
            var enemy = simulation.EnemyTeam.Enemies[index];
            if (!enemy.IsAlive)
                continue;

            if (enemy.IsAwaitingLifeBar || enemy.Buffs.HasTag(BuiltinBuffTags.TraitImmuneDamage))
            {
                anyProtected = true;
                continue;
            }

            if (!plan.TargetStates.TryGetValue(index, out var state) || !state.DepletedLifeBar)
                return MarbleRemainderReason.NoAvailableTarget;

            if (state.HadNextLifeBar)
                anyProtected = true;
        }

        return anyProtected
            ? MarbleRemainderReason.AllTargetsProtected
            : MarbleRemainderReason.AllEnemiesDefeated;
    }

    /// <summary>预演基础普攻与追打，填充每个目标的预计伤害 / 剩余生命。</summary>
    private static void ForecastPlan(CombatSimulation simulation, MarblePlan plan)
    {
        foreach (var attack in plan.Attacks)
        {
            var state = plan.TargetStates[attack.EnemyIndex];
            var target = new CombatTargetRef(ECombatSide.Enemy, attack.EnemyIndex);
            var preview = ForecastBaseDamage(simulation, plan.OwnerProfile, state, attack.Count, target);
            state.Health = MathF.Max(0f, state.Health - preview.HealthLoss);
            state.Shield = MathF.Max(0f, state.Shield - preview.ShieldAbsorbed);
            state.BaseDamage = preview.HealthLoss + preview.Overkill;
            state.BaseShieldAbsorbed = preview.ShieldAbsorbed;
            state.BaseHealthLoss = preview.HealthLoss;
            state.BaseOverflow = preview.Overkill;
            state.DepletedLifeBar = preview.HealthLoss >= state.InitialHealth;
        }

        foreach (var attack in plan.Attacks)
        {
            var state = plan.TargetStates[attack.EnemyIndex];
            if (state.DepletedLifeBar)
                continue;

            var target = new CombatTargetRef(ECombatSide.Enemy, attack.EnemyIndex);
            foreach (var (followerIndex, percent) in plan.Followers)
            {
                if (state.DepletedLifeBar)
                    break;

                var profile = BuildStrikeProfile(simulation, followerIndex, isOwner: false, percent / 100f);
                var rawDamage = ComputeRawStrikeDamage(simulation, profile, attack.Count, target);
                if (rawDamage <= 0f)
                    continue;

                var healthBefore = state.Health;
                var preview = DamagePipeline.Preview(
                    simulation,
                    new CombatTargetRef(ECombatSide.Player, followerIndex),
                    target,
                    rawDamage,
                    profile.Kind,
                    profile.Element,
                    absorbTargetShield: true,
                    targetShieldOverride: state.Shield,
                    targetHealthOverride: healthBefore);

                state.Health = MathF.Max(0f, healthBefore - preview.HealthLoss);
                state.Shield = MathF.Max(0f, state.Shield - preview.ShieldAbsorbed);
                state.FollowUpDamage += preview.HealthLoss;
                state.DepletedLifeBar = preview.HealthLoss >= healthBefore;
            }
        }
    }

    private static PreviewTargetState CreateTargetState(CombatSimulation simulation, int enemyIndex)
    {
        var enemy = simulation.EnemyTeam.Enemies[enemyIndex];
        var health = MathF.Max(0f, enemy.CurrentHpExact);
        var shield = MathF.Max(0f, enemy.Asc.GetCurrentValue(AttributeIds.Shield));
        return new PreviewTargetState(enemyIndex, health, shield, enemy.HasNextLifeBar);
    }

    /// <summary>标记的嘲讽约束：有正嘲讽时只能标记最高嘲讽的存活可受伤敌人（并列均可）。</summary>
    private static List<int> ResolveLegalMarks(CombatSimulation simulation, IReadOnlyList<int> candidates)
    {
        var legal = new List<int>();
        var highest = 0f;
        foreach (var index in candidates)
        {
            var taunt = MathF.Max(0f, simulation.EnemyTeam.Enemies[index].Asc.GetCurrentValue(AttributeIds.Taunt));
            if (taunt < highest)
                continue;
            if (taunt > highest)
            {
                legal.Clear();
                highest = taunt;
            }

            legal.Add(index);
        }

        return legal;
    }

    internal static List<int> LegalMarks(CombatSimulation simulation) =>
        ResolveLegalMarks(simulation, ResolveDamageableEnemies(simulation));

    private static List<int> ResolveDamageableEnemies(CombatSimulation simulation)
    {
        var candidates = new List<int>();
        for (var index = 0; index < simulation.EnemyTeam.Enemies.Count; index++)
        {
            var enemy = simulation.EnemyTeam.Enemies[index];
            if (!enemy.IsAlive || enemy.IsAwaitingLifeBar ||
                enemy.Buffs.HasTag(BuiltinBuffTags.TraitImmuneDamage))
            {
                continue;
            }

            candidates.Add(index);
        }

        return candidates;
    }

    /// <summary>
    /// 足以击杀所需的最小整数 Count（不足以击杀返回 null）：二分查找，Boss 只以当前血条为目标。
    /// </summary>
    private static int? FindMinimumKillCount(
        CombatSimulation simulation,
        StrikeProfile profile,
        PreviewTargetState state,
        int maxCount)
    {
        if (maxCount <= 0)
            return null;

        if (!WouldDeplete(simulation, profile, state, maxCount))
            return null;

        var low = 1;
        var high = maxCount;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (WouldDeplete(simulation, profile, state, middle))
                high = middle;
            else
                low = middle + 1;
        }

        return low;
    }

    private static bool WouldDeplete(
        CombatSimulation simulation,
        StrikeProfile profile,
        PreviewTargetState state,
        int count)
    {
        var target = new CombatTargetRef(ECombatSide.Enemy, state.EnemyIndex);
        var preview = ForecastBaseDamage(simulation, profile, state, count, target);
        return preview.HealthLoss >= state.InitialHealth;
    }

    private static DamageSettlementResult ForecastBaseDamage(
        CombatSimulation simulation,
        StrikeProfile profile,
        PreviewTargetState state,
        int count,
        CombatTargetRef target)
    {
        var rawDamage = ComputeRawStrikeDamage(simulation, profile, count, target);
        if (rawDamage <= 0f)
            return default;

        return DamagePipeline.Preview(
            simulation,
            new CombatTargetRef(ECombatSide.Player, profile.CharacterIndex),
            target,
            rawDamage,
            profile.Kind,
            profile.Element,
            absorbTargetShield: true,
            targetShieldOverride: state.Shield,
            targetHealthOverride: state.Health);
    }

    private static float ForecastBaseHealthLoss(
        CombatSimulation simulation,
        StrikeProfile profile,
        PreviewTargetState state,
        int count)
    {
        var target = new CombatTargetRef(ECombatSide.Enemy, state.EnemyIndex);
        return ForecastBaseDamage(simulation, profile, state, count, target).HealthLoss;
    }

    #endregion

    #region 伤害口径

    /// <summary>
    /// 一次打击（基础或追打）对单个目标的未过规则伤害：
    /// <c>max(0, Count × 攻击力 × 系数 × NormalAttackScale − 对应防御) × (1 + 增伤 + 受伤增加)</c>。
    /// </summary>
    private static float ComputeRawStrikeDamage(
        CombatSimulation simulation,
        StrikeProfile profile,
        int count,
        CombatTargetRef target)
    {
        if (count <= 0)
            return 0f;

        var attackTerm = count * profile.Attack * profile.Percent * CombatConstants.NormalAttackScale;
        var baseDamage = MathF.Max(0f, attackTerm - ResolveDefense(simulation, target, profile.Kind));
        if (baseDamage <= 0f)
            return 0f;

        var multiplier = DamageScaling.CombineBonuses(
            profile.DealtBonus,
            ResolveTakenScale(simulation, target, profile.Kind));
        var damage = baseDamage * multiplier;
        return float.IsFinite(damage) ? damage : 0f;
    }

    private static StrikeProfile BuildStrikeProfile(
        CombatSimulation simulation,
        int characterIndex,
        bool isOwner,
        float percent)
    {
        var character = simulation.PlayerTeam.Characters[characterIndex];
        var asc = character.Asc;
        var physicalAttack = asc.GetCurrentValue(AttributeIds.PhysicalAttack);
        var magicAttack = asc.GetCurrentValue(AttributeIds.MagicAttack);
        var kind = magicAttack > physicalAttack ? EDamageKind.Magical : EDamageKind.Physical;
        var attack = kind == EDamageKind.Magical ? magicAttack : physicalAttack;
        var dealtBonus =
            asc.GetCurrentValue(AttributeIds.DamageDealtScale) +
            asc.GetCurrentValue(AttributeIds.NormalAttackDamageDealtScale);

        return new StrikeProfile(
            characterIndex,
            isOwner,
            kind,
            character.Element,
            attack,
            dealtBonus,
            percent);
    }

    private static EDamageKind ResolveKind(CharacterBattleInstance character)
    {
        var asc = character.Asc;
        return asc.GetCurrentValue(AttributeIds.MagicAttack) >
            asc.GetCurrentValue(AttributeIds.PhysicalAttack)
            ? EDamageKind.Magical
            : EDamageKind.Physical;
    }

    /// <summary><c>NormalAttackShieldGainScale</c>：按该出手者本次造成的敌方实际生命损失转盾。</summary>
    private static void ApplyShieldGain(CharacterBattleInstance character, float healthLoss)
    {
        if (healthLoss <= 0f || !float.IsFinite(healthLoss))
            return;

        var scale = MathF.Max(0f, character.Asc.GetCurrentValue(AttributeIds.NormalAttackShieldGainScale));
        var gain = healthLoss * scale;
        if (gain <= 0f || !float.IsFinite(gain))
            return;

        character.Asc.SetBaseValue(
            AttributeIds.Shield,
            character.Asc.GetBaseValue(AttributeIds.Shield) + gain);
    }

    #endregion

    #region 预演 / 结算数据结构

    private readonly record struct StrikeProfile(
        int CharacterIndex,
        bool IsOwner,
        EDamageKind Kind,
        EElement Element,
        float Attack,
        float DealtBonus,
        float Percent);

    private sealed class MarblePlan
    {
        public int SlotIndex { get; set; } = -1;
        public string CharacterDefinitionId { get; set; } = "";
        public EDamageKind Kind { get; set; } = EDamageKind.Physical;
        public EElement Element { get; set; } = EElement.None;
        public int TotalCount { get; set; }
        public int RemainingCount { get; set; }
        public MarbleRemainderReason RemainingReason { get; set; } = MarbleRemainderReason.None;
        public StrikeProfile OwnerProfile { get; set; }
        public List<PlannedAttack> Attacks { get; } = [];
        public IReadOnlyList<(int Index, float Percent)> Followers { get; set; } =
            Array.Empty<(int Index, float Percent)>();
        public Dictionary<int, PreviewTargetState> TargetStates { get; } = new();
    }

    private sealed record PlannedAttack(int EnemyIndex, int Count, bool IsMarked);

    private sealed class PreviewTargetState(int enemyIndex, float health, float shield, bool hadNextLifeBar)
    {
        public int EnemyIndex { get; } = enemyIndex;
        public bool HadNextLifeBar { get; } = hadNextLifeBar;
        public float InitialHealth { get; } = health;
        public float Health { get; set; } = health;
        public float Shield { get; set; } = shield;
        public float BaseDamage { get; set; }
        public float BaseShieldAbsorbed { get; set; }
        public float BaseHealthLoss { get; set; }
        public float BaseOverflow { get; set; }
        public float FollowUpDamage { get; set; }
        public bool DepletedLifeBar { get; set; }
    }

    private sealed class SettledAttack
    {
        public int EnemyIndex { get; init; }
        public int Count { get; init; }
        public bool IsMarked { get; init; }
        public float HealthBefore { get; init; }
        public bool HadNextLifeBar { get; init; }
        public DamageSettlementResult Base { get; init; }
        public float FollowUpDamage { get; set; }
        public bool Depleted { get; set; }
    }

    private static MarbleAttackSettlement ToSettlement(MarblePlan plan)
    {
        var allocations = new List<MarbleAttackAllocation>(plan.Attacks.Count);
        foreach (var attack in plan.Attacks)
        {
            var state = plan.TargetStates[attack.EnemyIndex];
            allocations.Add(new MarbleAttackAllocation(
                attack.EnemyIndex,
                attack.Count,
                attack.IsMarked,
                state.BaseDamage,
                state.BaseShieldAbsorbed,
                state.BaseHealthLoss,
                state.BaseOverflow,
                state.FollowUpDamage,
                state.Health,
                state.DepletedLifeBar,
                state.DepletedLifeBar && state.HadNextLifeBar));
        }

        return CreateSettlement(plan, allocations);
    }

    private static MarbleAttackSettlement ToSettlement(
        MarblePlan plan,
        IReadOnlyList<SettledAttack> settled)
    {
        var allocations = new List<MarbleAttackAllocation>(settled.Count);
        foreach (var attack in settled)
        {
            var remainingHealth = MathF.Max(
                0f,
                attack.HealthBefore - attack.Base.HealthLoss - attack.FollowUpDamage);
            var depleted = remainingHealth <= 0f || attack.Depleted;
            allocations.Add(new MarbleAttackAllocation(
                attack.EnemyIndex,
                attack.Count,
                attack.IsMarked,
                attack.Base.HealthLoss + attack.Base.Overkill,
                attack.Base.ShieldAbsorbed,
                attack.Base.HealthLoss,
                attack.Base.Overkill,
                attack.FollowUpDamage,
                remainingHealth,
                depleted,
                depleted && attack.HadNextLifeBar));
        }

        return CreateSettlement(plan, allocations);
    }

    private static MarbleAttackSettlement CreateSettlement(
        MarblePlan plan,
        IReadOnlyList<MarbleAttackAllocation> allocations) =>
        new(
            plan.SlotIndex,
            plan.CharacterDefinitionId,
            plan.Kind,
            plan.Element,
            plan.TotalCount,
            plan.TotalCount - plan.RemainingCount,
            plan.RemainingCount,
            plan.RemainingReason,
            allocations);

    #endregion
}
