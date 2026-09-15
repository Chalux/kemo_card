using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;

namespace KemoCard.Mod.Combat.Runtime;

public sealed class TeamDomainManager : IGameplayEffectHookDispatcher
{
    private readonly CombatSimulation _sim;

    public TeamDomainManager(CombatSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        _sim = simulation;

        // 域 GE 挂在队伍 ASC 上，其 Hooks 需要能执行技能动作。生产环境此前从未给任何 ASC
        // 设置 HookDispatcher，导致内容里声明的 hooks.turnStart / turnEnd / onRemove 静默失效。
        // 说明：目前只有队伍 ASC 会被 FireTurnStartHooks / FireTurnEndHooks 驱动；
        // 角色与敌人 ASC 的 GameplayEffect 回合管线尚未接入，属未完成功能，不在本处覆盖范围。
        _sim.PlayerTeam.Asc.HookDispatcher = this;
        _sim.EnemyTeam.Asc.HookDispatcher = this;
    }

    #region IGameplayEffectHookDispatcher

    public void DispatchTurnStart(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions) =>
        DispatchHooks(effect, actions);

    public void DispatchTurnEnd(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions) =>
        DispatchHooks(effect, actions);

    public void DispatchRemove(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions) =>
        DispatchHooks(effect, actions);

    /// <summary>
    /// 执行 GE 声明的技能动作链，目标固定为队伍账本。
    /// </summary>
    /// <remarks>
    /// 规格 §1.3：v1 只有玩家侧存在队伍账本目标（<see cref="CombatTargetRef.PlayerTeam"/>），
    /// 敌方队伍账本尚未实装，因此敌方域 GE 的钩子退化为空放（与 <c>ResolveTeamLedgerTarget</c> 的既有语义一致）。
    /// </remarks>
    private void DispatchHooks(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count == 0)
        {
            return;
        }

        if (!ReferenceEquals(effect.Spec.TargetAsc, _sim.PlayerTeam.Asc))
        {
            return;
        }

        foreach (var actionRef in actions)
        {
            _sim.EffectExecutor.ExecuteSkillActionRef(
                actionRef,
                _sim,
                CombatTargetRef.PlayerTeam,
                [CombatTargetRef.PlayerTeam]);
        }
    }

    #endregion

    public bool TrySetPlayerDomain(string gameplayEffectId, IReadOnlyDictionary<string, object>? parameters = null)
        => TrySetDomain(_sim.PlayerTeam, gameplayEffectId, parameters);

    public bool TrySetEnemyDomain(string gameplayEffectId, IReadOnlyDictionary<string, object>? parameters = null)
        => TrySetDomain(_sim.EnemyTeam, gameplayEffectId, parameters);

    public void FireTurnStartHooks()
    {
        _sim.PlayerTeam.Asc.OnTurnStart();
        _sim.EnemyTeam.Asc.OnTurnStart();
    }

    public void FireTurnEndHooks()
    {
        if (_sim.PlayerTeam.ActiveDomain is not null)
            _sim.PlayerTeam.Asc.OnTurnEnd();
        if (_sim.EnemyTeam.ActiveDomain is not null)
            _sim.EnemyTeam.Asc.OnTurnEnd();
    }

    #region domain replacement

    private bool TrySetDomain(
        PlayerTeamState team,
        string gameplayEffectId,
        IReadOnlyDictionary<string, object>? parameters)
    {
        return TrySetDomain(
            team.Asc,
            team.ActiveDomain,
            newDomain => team.ActiveDomain = newDomain,
            gameplayEffectId,
            parameters);
    }

    private bool TrySetDomain(
        EnemyTeamState team,
        string gameplayEffectId,
        IReadOnlyDictionary<string, object>? parameters)
    {
        return TrySetDomain(
            team.Asc,
            team.ActiveDomain,
            newDomain => team.ActiveDomain = newDomain,
            gameplayEffectId,
            parameters);
    }

    #endregion

    private bool TrySetDomain(
        AbilitySystemComponent teamAsc,
        CombatDomain? oldDomain,
        Action<CombatDomain?> setDomain,
        string gameplayEffectId,
        IReadOnlyDictionary<string, object>? parameters)
    {
        if (!_sim.Definitions.Store.TryGetGameplayEffect(gameplayEffectId, out var gameplayEffectDef))
            return false;

        if (oldDomain is not null)
            teamAsc.RemoveActiveEffect(oldDomain.ActiveEffectHandle);

        var result = teamAsc.ApplyGameplayEffect(
            new GameplayEffectSpec(
                BuildInfiniteDomainEffect(gameplayEffectDef),
                sourceAsc: teamAsc,
                targetAsc: teamAsc,
                setByCaller: BuildSetByCaller(parameters)));
        if (!result.Success || result.Handle is null)
        {
            setDomain(null);
            return false;
        }

        setDomain(new CombatDomain(gameplayEffectId, result.Handle.Value, parameters));
        return true;
    }

    private static GameplayEffectDefDto BuildInfiniteDomainEffect(GameplayEffectDefDto source)
    {
        return new GameplayEffectDefDto
        {
            Id = source.Id,
            DisplayNameId = source.DisplayNameId,
            DurationPolicy = EDurationPolicy.Infinite,
            DurationTurns = 0,
            PeriodTurns = source.PeriodTurns,
            StackingPolicy = source.StackingPolicy,
            MaxStacks = source.MaxStacks,
            Modifiers = [.. source.Modifiers],
            Executions = [.. source.Executions],
            GrantedTags = [.. source.GrantedTags],
            ApplicationRequiredTags = [.. source.ApplicationRequiredTags],
            ApplicationBlockedTags = [.. source.ApplicationBlockedTags],
            OngoingRequiredTags = [.. source.OngoingRequiredTags],
            ImmunityTags = [.. source.ImmunityTags],
            RemoveEffectsWithTags = [.. source.RemoveEffectsWithTags],
            Hooks = source.Hooks,
        };
    }

    private static Dictionary<string, float>? BuildSetByCaller(IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return null;

        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
        {
            if (TryConvertFloat(value, out var number))
                result[key] = number;
        }

        return result.Count == 0 ? null : result;
    }

    private static bool TryConvertFloat(object? value, out float number)
    {
        number = 0f;
        if (value is null)
            return false;

        switch (value)
        {
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case short shortValue:
                number = shortValue;
                return true;
            case byte byteValue:
                number = byteValue;
                return true;
            case float floatValue:
                number = floatValue;
                return true;
            case double doubleValue:
                number = (float)doubleValue;
                return true;
            case decimal decimalValue:
                number = (float)decimalValue;
                return true;
            default:
                return float.TryParse(value.ToString(), out number);
        }
    }
}