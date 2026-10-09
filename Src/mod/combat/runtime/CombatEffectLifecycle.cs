using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Executions;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Presentation;

namespace KemoCard.Mod.Combat.Runtime;

/// <summary>战斗持有者的 GE 驱动。整批快照后再执行，批次内新增效果留到下一批。</summary>
internal sealed class CombatEffectLifecycle(CombatSimulation simulation)
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<AbilitySystemComponent, HolderIdentity> _sources = new();
    private readonly ExecutionRunner _executionRunner = new();

    #region 持有者生命周期

    public void AttachHolders()
    {
        foreach (var (holder, asc) in EnumerateHolders())
        {
            _sources.GetValue(asc, _ => new HolderIdentity(holder));
            asc.HookDispatcher = new HookDispatcher(simulation, holder, this);
            asc.OnPeriodicTriggered = effect => ExecutePeriodic(holder, asc, effect);
        }
    }

    public void TurnStart() => Tick(start: true);
    public void TurnEnd() => Tick(start: false);

    private void Tick(bool start)
    {
        var batch = EnumerateHolders().Select(pair =>
            (pair.Holder, pair.Asc, Effects: pair.Asc.ActiveEffects.ToArray())).ToArray();
        foreach (var (_, asc, effects) in batch)
        {
            if (start)
                asc.OnTurnStart(effects);
            else
                asc.OnTurnEnd(effects);
        }
        simulation.FlushOnDamagedHits();
    }

    private IEnumerable<(CombatTargetRef Holder, AbilitySystemComponent Asc)> EnumerateHolders()
    {
        yield return (CombatTargetRef.PlayerTeam, simulation.PlayerTeam.Asc);
        yield return (new(ECombatSide.Enemy, -1), simulation.EnemyTeam.Asc);
        for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
            yield return (new(ECombatSide.Player, i), simulation.PlayerTeam.Characters[i].Asc);
        for (var i = 0; i < simulation.EnemyTeam.Enemies.Count; i++)
            if (simulation.EnemyTeam.Enemies[i].IsAlive)
                yield return (new(ECombatSide.Enemy, i), simulation.EnemyTeam.Enemies[i].Asc);
    }

    private CombatTargetRef ResolveSource(ActiveGameplayEffect effect, CombatTargetRef fallback)
    {
        if (effect.Spec.SourceAsc is not null && _sources.TryGetValue(effect.Spec.SourceAsc, out var identity))
            return identity.Target;
        return fallback;
    }

    private void ExecutePeriodic(CombatTargetRef holder, AbilitySystemComponent asc, ActiveGameplayEffect effect)
    {
        var source = ResolveSource(effect, holder);
        var loss = 0f;
        var spec = new GameplayEffectSpec(effect.Def, effect.Spec.SourceAsc, asc,
            effect.Spec.SetByCaller.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            damageReceiver: (execution, amount) =>
            {
                var type = DamageTypeParser.Parse(execution.DamageType, execution.Element);
                var result = DamagePipeline.Settle(simulation, source, holder, amount, effect.Def.Id, type.Kind, type.Element);
                if (holder.Side == ECombatSide.Enemy)
                    loss += result.HealthLoss;
            });
        using var scope = simulation.EffectBudget.TryEnter();
        if (scope is null)
            return;
        _executionRunner.Run(spec, asc);
        if (source.Side == ECombatSide.Player && loss > 0f && effect.Def.LifestealScale > 0f)
        {
            var before = simulation.PlayerTeam.SharedHpExact;
            simulation.PlayerTeam.HealShared(loss * effect.Def.LifestealScale);
            PresentationEmitter.EmitHeal(simulation, source, CombatTargetRef.PlayerTeam, simulation.PlayerTeam.SharedHpExact - before);
        }
    }

    #endregion

    private sealed record HolderIdentity(CombatTargetRef Target);

    private sealed class HookDispatcher(CombatSimulation sim, CombatTargetRef holder, CombatEffectLifecycle owner)
        : IGameplayEffectHookDispatcher
    {
        public void DispatchApply(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions) => Dispatch(effect, actions);
        public void DispatchStackChanged(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions) => Dispatch(effect, actions);
        public void DispatchTurnStart(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions) => Dispatch(effect, actions);
        public void DispatchTurnEnd(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions) => Dispatch(effect, actions);
        public void DispatchRemove(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions) => Dispatch(effect, actions);

        private void Dispatch(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions)
        {
            // v1 没有敌方队伍账本载荷；敌方个体钩子照常执行。
            if (holder.Side == ECombatSide.Enemy && holder.Index < 0)
                return;
            foreach (var action in actions)
                sim.EffectExecutor.ExecuteSkillActionRef(action, sim, owner.ResolveSource(effect, holder), [holder]);
        }
    }
}