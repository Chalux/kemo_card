using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Executions;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Mod.Combat.Effects;

public sealed class GameplayEffectApplicator
{
    private readonly GameDefinitionRegistry _registry;

    public GameplayEffectApplicator(GameDefinitionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public bool ApplyToTargets(
        CombatSimulation simulation,
        CombatTargetRef source,
        IReadOnlyList<CombatTargetRef> targets,
        string gameplayEffectId,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(targets);
        if (!_registry.Store.TryGetGameplayEffect(gameplayEffectId, out var def))
            return false;
        using var scope = simulation.EffectBudget.TryEnter();
        if (scope is null)
            return false;

        var sourceAsc = CombatGasBridge.ResolveSourceAsc(simulation, source);
        var setByCaller = ContentParameters.ToSetByCaller(parameters) ?? new Dictionary<string, float>(StringComparer.Ordinal);
        // 两条卡牌载荷通道共用，宿主覆盖此键，非卡牌上下文不能沿用内容传入的增伤。
        setByCaller[DamageExecution.SetByCallerCardDamageBonus] = DamageScaling.ResolveCardDamageBonus(simulation, source);
        var descriptor = ResolveDamageDescriptor(def);

        var applied = false;
        // 吸血（2026-09-24）：统计本 GE 对**非玩家侧**目标造成的实际生命损失，结算完再按系数回一次队伍账本。
        var damageDealt = 0f;
        foreach (var target in targets)
        {
            var targetAsc = CombatGasBridge.ResolveTargetAsc(simulation, target);
            if (targetAsc is null)
                continue;

            // 免疫在投放前拒绝整条对应效果，不执行即时修饰、标签和应用/移除钩子。
            if (IsImmuneToEffect(simulation, target, def))
            {
                // 合法效果已被免疫处理，不能让调用方再回落到无标签的直伤/治疗载荷。
                applied = true;
                continue;
            }

            var ownHealthLoss = 0f;
            var damage = new List<(ExecutionDefDto Execution, float Amount)>();

            // 玩家侧目标的 Health 变化在此被转到共享账本（规格 §1.2 的「应用后转移」）；
            // 扣血变化量统一过伤害规则管线（DamagePipeline），并带上该 GE 声明的伤害维度
            // （damageType/element → 物理/魔法/元素 + 属性标签），否则规则只能看到"物理无属性"。
            var result = targetAsc.ApplyGameplayEffect(new GameplayEffectSpec(def, sourceAsc, targetAsc, setByCaller,
                damageReceiver: (execution, amount) => damage.Add((execution, amount)),
                instantExecutionScope: apply =>
                {
                    ownHealthLoss += SharedHpSettlement.RunTransferred(simulation, source, target, apply, gameplayEffectId, descriptor.Kind, descriptor.Element);
                    foreach (var (execution, amount) in damage)
                    {
                        var damageType = DamageTypeParser.Parse(execution.DamageType, execution.Element);
                        ownHealthLoss += DamagePipeline.Settle(simulation, source, target, amount, gameplayEffectId, damageType.Kind, damageType.Element).HealthLoss;
                    }
                }));
            applied |= result.Success;

            if (target.Side != ECombatSide.Player)
            {
                damageDealt += ownHealthLoss;
            }

            // 规格 §2.5：非免疫角色挂上封印后立刻清标记并视作已行动。
            if (target.Side == ECombatSide.Player &&
                target.Index >= 0 &&
                target.Index < simulation.PlayerTeam.Characters.Count &&
                simulation.PlayerTeam.Characters[target.Index].IsSealed)
            {
                CombatStateMachine.EnforceSeal(simulation, target.Index);
            }
        }

        // 吸血只对"玩家来源 → 非玩家目标"成立：账本是玩家侧的血，敌人打自己不该给玩家回血。
        if (def.LifestealScale > 0f && damageDealt > 0f && source.Side == ECombatSide.Player)
        {
            var before = simulation.PlayerTeam.SharedHpExact;
            simulation.PlayerTeam.HealShared(damageDealt * def.LifestealScale);
            PresentationEmitter.EmitHeal(
                simulation,
                source,
                CombatTargetRef.PlayerTeam,
                simulation.PlayerTeam.SharedHpExact - before);
        }

        return applied;
    }

    /// <summary>对应免疫特征拒绝整条 GE，避免先运行载荷再移除的副作用。</summary>
    private static bool IsImmuneToEffect(
        CombatSimulation simulation,
        CombatTargetRef target,
        GameplayEffectDefDto def)
    {
        if (target.Side != ECombatSide.Player ||
            target.Index < 0 ||
            target.Index >= simulation.PlayerTeam.Characters.Count)
        {
            return false;
        }

        var buffs = simulation.PlayerTeam.Characters[target.Index].Buffs;
        return (def.GrantedTags.Contains(CombatConstants.VirusTag, StringComparer.Ordinal) && buffs.HasTag(BuiltinBuffTags.TraitImmuneVirus)) ||
            (def.GrantedTags.Contains(CombatConstants.PoisonTag, StringComparer.Ordinal) && buffs.HasTag(BuiltinBuffTags.TraitImmunePoison)) ||
            (def.GrantedTags.Contains(CombatConstants.SealedTag, StringComparer.Ordinal) && buffs.HasTag(BuiltinBuffTags.TraitImmuneSeal));
    }

    public bool RemoveFromTargets(
        CombatSimulation simulation,
        IReadOnlyList<CombatTargetRef> targets,
        string gameplayEffectId)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(targets);

        var removed = false;
        foreach (var target in targets)
        {
            var targetAsc = CombatGasBridge.ResolveTargetAsc(simulation, target);
            if (targetAsc is null)
                continue;

            var handles = targetAsc.ActiveEffects
                .Where(effect => string.Equals(effect.Def.Id, gameplayEffectId, StringComparison.Ordinal))
                .Select(effect => effect.Handle)
                .ToArray();
            foreach (var handle in handles)
                removed |= targetAsc.RemoveActiveEffect(handle);
        }

        return removed;
    }

    /// <summary>
    /// 取该 Gameplay Effect 的伤害维度声明（第一条 <c>Damage</c> 执行上的 <c>damageType</c>/<c>element</c>）。
    /// 无伤害执行时回落到缺省（物理 + 无属性）——增伤/治疗类 GE 的伤害包语义本就无关紧要。
    /// </summary>
    private static DamageTypeSpec ResolveDamageDescriptor(GameplayEffectDefDto def)
    {
        foreach (var execution in def.Executions)
        {
            if (string.Equals(execution.Kind, DamageExecution.DamageKind, StringComparison.OrdinalIgnoreCase))
                return DamageTypeParser.Parse(execution.DamageType, execution.Element);
        }

        return DamageTypeSpec.Default;
    }
}