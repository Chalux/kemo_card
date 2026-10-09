using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Buffs;

namespace KemoCard.Mod.Combat.Runtime;

public sealed class TeamDomainManager
{
    private readonly CombatSimulation _sim;

    /// <summary>带时长的玩家领域剩余回合数（<c>null</c> = 不自动收起）；回合结束时递减，归零收起。</summary>
    private int? _playerDomainTurnsLeft;
    private readonly Dictionary<Guid, List<OwnedDomainBuff>> _domainBuffs = [];

    public TeamDomainManager(CombatSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        _sim = simulation;

    }

    public bool TrySetPlayerDomain(string gameplayEffectId, IReadOnlyDictionary<string, object>? parameters = null)
        => TrySetPlayerDomain(gameplayEffectId, parameters, turns: null);

    /// <summary>
    /// 展开玩家领域（<paramref name="turns"/> = 持续回合数，<c>null</c> = 不自动收起）。
    /// 「领域展开 2 回合」即 <c>turns: 2</c>：到期在回合结束时由 <see cref="FireTurnEndHooks"/> 收起。
    /// </summary>
    public bool TrySetPlayerDomain(
        string gameplayEffectId,
        IReadOnlyDictionary<string, object>? parameters,
        int? turns)
    {
        var success = TrySetDomain(_sim.PlayerTeam, gameplayEffectId, parameters, turns);
        RefreshDomainBuffs();
        return success;
    }

    public bool TrySetEnemyDomain(string gameplayEffectId, IReadOnlyDictionary<string, object>? parameters = null)
    {
        var success = TrySetDomain(_sim.EnemyTeam, gameplayEffectId, parameters);
        RefreshDomainBuffs();
        return success;
    }

    public void FireTurnStartHooks() => _sim.EffectLifecycle.TurnStart();

    public void FireTurnEndHooks()
    {
        var handle = _sim.PlayerTeam.ActiveDomain?.ActiveEffectHandle;
        _sim.EffectLifecycle.TurnEnd();
        if (_sim.PlayerTeam.ActiveDomain?.ActiveEffectHandle == handle)
            TickPlayerDomainDuration();
    }
    /// <summary>
    /// 带时长的领域在回合结束时递减，归零则收起（移除 GE + 清空领域槽）。
    /// 无时长的领域（<c>turns</c> 缺省）不受影响，直到被新领域顶替。
    /// </summary>
    private void TickPlayerDomainDuration()
    {
        if (_playerDomainTurnsLeft is not > 0 || _sim.PlayerTeam.ActiveDomain is null)
            return;

        _playerDomainTurnsLeft--;
        if (_playerDomainTurnsLeft > 0)
            return;

        _playerDomainTurnsLeft = null;
        ClearPlayerDomain();
    }

    /// <summary>收起当前玩家领域（移除队伍 ASC 上的域 GE 并清空领域槽）。</summary>
    public void ClearPlayerDomain()
    {
        var domain = _sim.PlayerTeam.ActiveDomain;
        if (domain is null)
            return;

        _sim.PlayerTeam.ActiveDomain = null;
        _playerDomainTurnsLeft = null;
        RemoveDomainBuffs(domain.ActiveEffectHandle);
        _sim.PlayerTeam.Asc.RemoveActiveEffect(domain.ActiveEffectHandle);
    }

    #region domain replacement

    private bool TrySetDomain(
        PlayerTeamState team,
        string gameplayEffectId,
        IReadOnlyDictionary<string, object>? parameters,
        int? turns)
    {
        return TrySetDomain(
            team.Asc,
            team.ActiveDomain,
            newDomain =>
            {
                var previous = team.ActiveDomain;
                team.ActiveDomain = newDomain;
                _playerDomainTurnsLeft = newDomain is not null && turns is > 0 ? turns : null;
                if (previous is not null && previous.ActiveEffectHandle != newDomain?.ActiveEffectHandle)
                {
                    RemoveDomainBuffs(previous.ActiveEffectHandle);
                    team.Asc.RemoveActiveEffect(previous.ActiveEffectHandle);
                }
            },
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
            newDomain =>
            {
                var previous = team.ActiveDomain;
                team.ActiveDomain = newDomain;
                if (previous is not null && previous.ActiveEffectHandle != newDomain?.ActiveEffectHandle)
                {
                    RemoveDomainBuffs(previous.ActiveEffectHandle);
                    team.Asc.RemoveActiveEffect(previous.ActiveEffectHandle);
                }
            },
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
            setDomain(null);

        var result = teamAsc.ApplyGameplayEffect(
            new GameplayEffectSpec(
                BuildInfiniteDomainEffect(gameplayEffectDef),
                sourceAsc: teamAsc,
                targetAsc: teamAsc,
                setByCaller: ContentParameters.ToSetByCaller(parameters),
                activeEffectRegistered: active => setDomain(new CombatDomain(gameplayEffectId, active.Handle,
                    parameters is null ? null : ContentParameters.Merge(null, parameters)))));
        if (!result.Success || result.Handle is null)
        {
            setDomain(null);
            return false;
        }

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
            DomainBuffRefs = [.. source.DomainBuffRefs],
        };
    }

    #region domain owned buffs

    /// <summary>领域 Buff 覆盖双方；新敌人入场后补齐，原有实例不重复投放。</summary>
    internal void RefreshDomainBuffs()
    {
        using var scope = _sim.EffectBudget.TryEnter();
        if (scope is null)
            return;
        foreach (var domain in new[] { _sim.PlayerTeam.ActiveDomain, _sim.EnemyTeam.ActiveDomain })
        {
            if (domain is null || !_sim.Definitions.Store.TryGetGameplayEffect(domain.GameplayEffectId, out var def) || def.DomainBuffRefs.Count == 0)
                continue;
            if (!_domainBuffs.TryGetValue(domain.ActiveEffectHandle, out var owned))
                _domainBuffs[domain.ActiveEffectHandle] = owned = [];
            foreach (var (holder, container) in EnumerateCombatants())
            {
                if (!IsDomainActive(domain.ActiveEffectHandle))
                    break;
                foreach (var reference in def.DomainBuffRefs)
                {
                    if (!IsDomainActive(domain.ActiveEffectHandle))
                        break;
                    if (owned.Any(entry => ReferenceEquals(entry.Container, container) && entry.Instance.Def.Id == reference.BuffId && container.All.Contains(entry.Instance)))
                        continue;
                    var instance = _sim.Buffs.AddDomainBuff(_sim, holder, reference);
                    if (instance is null)
                        continue;
                    // onApply 可顶替领域：撤销刚创建的实例，避免旧领域留下孤立加成。
                    if (!IsDomainActive(domain.ActiveEffectHandle))
                    {
                        _sim.Buffs.RemoveDomainBuff(_sim, holder, container, instance);
                        break;
                    }
                    owned.Add(new(holder, container, instance));
                }
            }
        }
    }

    private bool IsDomainActive(Guid handle) =>
        _sim.PlayerTeam.ActiveDomain?.ActiveEffectHandle == handle || _sim.EnemyTeam.ActiveDomain?.ActiveEffectHandle == handle;

    private IEnumerable<(CombatTargetRef Holder, BuffContainer Container)> EnumerateCombatants()
    {
        for (var i = 0; i < _sim.PlayerTeam.Characters.Count; i++)
            yield return (new(ECombatSide.Player, i), _sim.PlayerTeam.Characters[i].Buffs);
        for (var i = 0; i < _sim.EnemyTeam.Enemies.Count; i++)
            if (_sim.EnemyTeam.Enemies[i].IsAlive)
                yield return (new(ECombatSide.Enemy, i), _sim.EnemyTeam.Enemies[i].Buffs);
    }

    private void RemoveDomainBuffs(Guid handle)
    {
        if (!_domainBuffs.Remove(handle, out var owned))
            return;
        foreach (var entry in owned)
            _sim.Buffs.RemoveDomainBuff(_sim, entry.Holder, entry.Container, entry.Instance);
    }

    private sealed record OwnedDomainBuff(CombatTargetRef Holder, BuffContainer Container, BuffInstance Instance);

    #endregion
}