using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.Ai;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Combat.Gas;

namespace KemoCard.Mod.Combat.Runtime;

public sealed class CombatSimulation : IDisposable
{
    private readonly CombatStateMachine _stateMachine;
    private readonly TeamMaxHealthCoordinator _teamMaxHealthCoordinator;
    private long _nextQueueSequence = 1;

    public PlayerTeamState PlayerTeam { get; }
    public EnemyTeamState EnemyTeam { get; }
    public CombatRuleEngine Rules { get; }
    public GameDefinitionRegistry Definitions { get; }
    public CardExecutionQueue CardQueue { get; }
    public CombatEffectExecutor EffectExecutor { get; }
    public TeamDomainManager DomainManager { get; }
    public EnemyAiController EnemyAi { get; }
    public IContentEffectScriptHost ScriptHost { get; }
    public string ModId { get; }
    public BattleDto? Battle { get; }
    public int CurrentWaveIndex { get; private set; }
    public int WaveCount => Battle?.Waves.Count ?? 1;
    public bool HasMoreWaves => CurrentWaveIndex + 1 < WaveCount;
    public int TurnNumber { get; private set; } = 1;
    public int RunSeed { get; }
    public ECombatPhase Phase => _stateMachine.Phase;

    /// <summary>首个玩家阶段：当前能量不 +1、不按公式抽牌（规格 §6.2）。</summary>
    public bool IsFirstPlayerPhase { get; private set; } = true;

    /// <summary>开战注入的技能序列，由调用方按规格 §6.1 排好序（被动在前、修饰在后）。</summary>
    public IReadOnlyList<BattleStartSkillEntry> BattleStartSkills { get; }

    /// <summary>规格 §4.6：当前中途弃牌通道；由状态机在进入主动技 / 执行阶段 / 其它上下文时设置。</summary>
    public EDiscardChannel CurrentDiscardChannel { get; private set; } = EDiscardChannel.Other;

    /// <summary>规格 §4.3：战斗中途即时抽牌被拒绝的次数（诊断计数器，禁止 GD.Print）。</summary>
    public int BlockedMidDrawCount { get; private set; }

    internal HostRng EnemyAiRng { get; }
    internal HostRng DrawRng { get; }

    /// <summary>执行阶段单体目标失效时的均匀重选流（规格 §2.4）。</summary>
    internal HostRng RetargetRng { get; }

    /// <summary>规格 §4.6：中途弃牌独立随机流。</summary>
    internal HostRng DiscardRng { get; }

    public CombatSimulation(
        PlayerTeamState playerTeam,
        EnemyTeamState enemyTeam,
        CombatRuleEngine rules,
        GameDefinitionRegistry definitions,
        ECombatPhase initialPhase = ECombatPhase.BattleStart,
        int runSeed = 1,
        BattleDto? battle = null,
        int currentWaveIndex = 0,
        EnemyAiScriptInvoker? enemyAiScriptInvoker = null,
        IContentEffectScriptHost? scriptHost = null,
        string modId = "test.mod",
        IReadOnlyList<BattleStartSkillEntry>? battleStartSkills = null)
    {
        ArgumentNullException.ThrowIfNull(playerTeam);
        ArgumentNullException.ThrowIfNull(enemyTeam);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        PlayerTeam = playerTeam;
        EnemyTeam = enemyTeam;
        Rules = rules;
        Definitions = definitions;
        Battle = battle;
        CurrentWaveIndex = currentWaveIndex;
        RunSeed = runSeed;
        ScriptHost = scriptHost ?? new NullContentEffectScriptHost();
        ModId = modId;
        BattleStartSkills = battleStartSkills ?? [];
        CardQueue = new CardExecutionQueue();
        EnemyAiRng = new HostRng(runSeed, "combat.ai");
        DrawRng = new HostRng(runSeed, "combat.draw");
        RetargetRng = new HostRng(runSeed, "combat.retarget");
        DiscardRng = new HostRng(runSeed, "combat.discard");
        EffectExecutor = new CombatEffectExecutor(definitions, rules);
        DomainManager = new TeamDomainManager(this);
        EnemyAi = new EnemyAiController(definitions, enemyAiScriptInvoker, modId, runSeed);
        _teamMaxHealthCoordinator = new TeamMaxHealthCoordinator(PlayerTeam);
        _stateMachine = new CombatStateMachine(initialPhase);
    }

    public CombatApplyResult TryApply(ICombatCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _stateMachine.TryApply(this, command);
    }

    public CombatContext CreateContext() => new(this, TurnNumber);

    public void TransitionTo(ECombatPhase phase) => _stateMachine.TransitionTo(phase);

    public void AdvancePhase() => _stateMachine.Advance(this);

    /// <summary>执行 BattleStart 管线（规格 §6.1），结束后停在首个玩家阶段。</summary>
    public void RunBattleStart() => _stateMachine.RunBattleStart(this);

    internal long AllocateQueueSequence() => _nextQueueSequence++;

    internal void MarkFirstPlayerPhaseDone() => IsFirstPlayerPhase = false;

    internal void IncrementTurnNumber() => TurnNumber++;

    /// <summary>测试与状态机共用：切换当前弃牌通道。</summary>
    public void SetDiscardChannel(EDiscardChannel channel) => CurrentDiscardChannel = channel;

    internal void CountBlockedMidDraw() => BlockedMidDrawCount++;

    public void CheckEndConditions()
    {
        if (Phase is ECombatPhase.Victory or ECombatPhase.Defeat)
            return;

        var decision = new EndDecision { Kind = EEndDecisionKind.None };
        Rules.DispatchCheckEndCondition(CreateContext(), ref decision);

        if (decision.Kind == EEndDecisionKind.Defeat)
        {
            TransitionTo(ECombatPhase.Defeat);
            return;
        }

        if (decision.Kind == EEndDecisionKind.Victory)
        {
            TransitionTo(ECombatPhase.Victory);
            return;
        }

        if (PlayerTeam.IsDefeated)
        {
            TransitionTo(ECombatPhase.Defeat);
            return;
        }

        if (!EnemyTeam.AllDefeated)
            return;

        if (HasMoreWaves)
        {
            AdvanceToNextWave();
            return;
        }

        TransitionTo(ECombatPhase.Victory);
    }

    private void AdvanceToNextWave()
    {
        if (Battle is null)
            return;

        TransitionTo(ECombatPhase.WaveTransition);
        CurrentWaveIndex++;
        var enemies = CombatSimulationFactory.SpawnWaveEnemies(
            Battle.Waves[CurrentWaveIndex],
            Definitions,
            out var error);
        if (enemies is null)
            throw new InvalidOperationException(error ?? "下一波敌人生成失败。");

        EnemyTeam.ReplaceEnemies(enemies);
        TransitionTo(ECombatPhase.Player);
    }

    public void Dispose()
    {
        _teamMaxHealthCoordinator.Dispose();
    }
}