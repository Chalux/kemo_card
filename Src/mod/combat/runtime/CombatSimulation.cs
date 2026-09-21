using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.Ai;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.NormalAttack;
using KemoCard.Mod.Combat.Orbs;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Combat.Gas;

namespace KemoCard.Mod.Combat.Runtime;

public sealed class CombatSimulation : IDisposable
{
    private readonly CombatStateMachine _stateMachine;
    private readonly TeamMaxHealthCoordinator _teamMaxHealthCoordinator;
    private readonly List<PlayedCardRecord> _playedThisTurn = [];
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

    /// <summary>Buff 运行时：投放/叠层/钩子/时长 tick/驱散 的统一编排入口。</summary>
    public BuffRuntime Buffs { get; }

    /// <summary>充能球运行时：全队共享队列的授予、触发与回合结束产出。</summary>
    public OrbRuntime Orbs { get; }

    /// <summary>普通攻击运行时：每回合卡牌结算完成后自动执行一次（槽位轮转）。</summary>
    public NormalAttackRuntime NormalAttacks { get; }

    public string ModId { get; }
    public BattleDto? Battle { get; }
    public int CurrentWaveIndex { get; private set; }
    public int WaveCount => Battle?.Waves.Count ?? 1;
    public bool HasMoreWaves => CurrentWaveIndex + 1 < WaveCount;
    public int TurnNumber { get; private set; } = 1;

    /// <summary>当前波次（阶层）内的回合计数，换波清零；"波内每 N 回合"类钩子按此分档。</summary>
    public int TurnsIntoWave { get; private set; }

    public int RunSeed { get; }
    public ECombatPhase Phase => _stateMachine.Phase;

    /// <summary>首个玩家阶段：当前能量不 +1、不按公式抽牌（规格 §6.2）。</summary>
    public bool IsFirstPlayerPhase { get; private set; } = true;

    /// <summary>开战注入的技能序列，由调用方按规格 §6.1 排好序（被动在前、修饰在后）。</summary>
    public IReadOnlyList<BattleStartSkillEntry> BattleStartSkills { get; }

    /// <summary>开战注入的被动 buff：技能注入之后、冻结 SharedHp 之前挂载。</summary>
    public IReadOnlyList<BattleStartBuffEntry> InitialBuffs { get; }

    /// <summary>规格 §4.6：当前中途弃牌通道；由状态机在进入主动技 / 执行阶段 / 其它上下文时设置。</summary>
    public EDiscardChannel CurrentDiscardChannel { get; private set; } = EDiscardChannel.Other;

    /// <summary>
    /// 当前结算卡牌适用的连携加成（与 DamageDealtScale 同桶加算）。
    /// 由状态机在单卡结算区间设置，结算完归零——卡牌上下文之外恒为 0。
    /// </summary>
    public float CurrentChainBonus { get; private set; }

    /// <summary>规格 §4.3：战斗中途即时抽牌被拒绝的次数（诊断计数器，禁止 GD.Print）。</summary>
    public int BlockedMidDrawCount { get; private set; }

    internal HostRng EnemyAiRng { get; }
    internal HostRng DrawRng { get; }

    /// <summary>执行阶段单体目标失效时的均匀重选流（规格 §2.4）。</summary>
    internal HostRng RetargetRng { get; }

    /// <summary>规格 §4.6：中途弃牌独立随机流。</summary>
    internal HostRng DiscardRng { get; }

    /// <summary>充能球回合结束产出（平局随机）独立随机流。</summary>
    internal HostRng OrbRng { get; }

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
        IReadOnlyList<BattleStartSkillEntry>? battleStartSkills = null,
        IReadOnlyList<BattleStartBuffEntry>? initialBuffs = null)
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
        InitialBuffs = initialBuffs ?? [];
        CardQueue = new CardExecutionQueue();
        EnemyAiRng = new HostRng(runSeed, "combat.ai");
        DrawRng = new HostRng(runSeed, "combat.draw");
        RetargetRng = new HostRng(runSeed, "combat.retarget");
        DiscardRng = new HostRng(runSeed, "combat.discard");
        OrbRng = new HostRng(runSeed, "combat.orb");
        EffectExecutor = new CombatEffectExecutor(definitions);
        Buffs = new BuffRuntime(definitions, EffectExecutor);
        EffectExecutor.AttachBuffRuntime(Buffs);
        Orbs = new OrbRuntime(definitions);
        NormalAttacks = new NormalAttackRuntime();
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

    internal void IncrementTurnsIntoWave() => TurnsIntoWave++;

    /// <summary>测试与状态机共用：切换当前弃牌通道。</summary>
    public void SetDiscardChannel(EDiscardChannel channel) => CurrentDiscardChannel = channel;

    /// <summary>状态机专用：设置/清零当前结算卡牌的连携加成（结算区间之外恒为 0）。</summary>
    internal void SetChainBonus(float bonus) => CurrentChainBonus = bonus;

    internal void CountBlockedMidDraw() => BlockedMidDrawCount++;

    /// <summary>本回合已打出的卡牌登记（充能球回合结束统计口径；含空放——牌已离手即算打出）。</summary>
    internal void RecordPlayedCard(string cardId, int characterIndex) =>
        _playedThisTurn.Add(new PlayedCardRecord(cardId, characterIndex));

    /// <summary>取走本回合出牌记录并清空（每回合结束产出充能球时调用一次）。</summary>
    internal IReadOnlyList<PlayedCardRecord> TakePlayedThisTurn()
    {
        if (_playedThisTurn.Count == 0)
            return [];

        var records = _playedThisTurn.ToArray();
        _playedThisTurn.Clear();
        return records;
    }

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

        // 玩家账本归零优先于胜利：同归于尽必须判负，不能让规则提供的 Victory 覆盖掉。
        if (PlayerTeam.IsDefeated)
        {
            TransitionTo(ECombatPhase.Defeat);
            return;
        }

        if (decision.Kind == EEndDecisionKind.Victory)
        {
            TransitionTo(ECombatPhase.Victory);
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
        TurnsIntoWave = 0;
        // 新波次的敌人是全新的实例（buff 容器为空）：必须重新挂载内容声明的开战 buff，
        // 否则 EnemyDto.buffRefs 只在第一波生效。顺序与 BattleStart 一致——先挂 buff 再发阶层钩子。
        ApplyEnemyInitialBuffs();
        // 阶层开始钩子（波内每 N 回合的计时基准同时归零）。
        Buffs.FireWaveStart(this);
        TransitionTo(ECombatPhase.Player);
    }

    /// <summary>
    /// 把每个敌人 <c>buffRefs</c> 声明的 buff 挂到它自己身上（与玩家侧开战被动注入对称）。
    /// 未找到敌人定义时软失败跳过。BattleStart 与每次换波都要调用一次。
    /// </summary>
    internal void ApplyEnemyInitialBuffs()
    {
        for (var i = 0; i < EnemyTeam.Enemies.Count; i++)
        {
            var enemy = EnemyTeam.Enemies[i];
            if (!Definitions.Store.TryGetEnemy(enemy.DefinitionId, out var definition))
                continue;

            foreach (var buffRef in definition.BuffRefs)
            {
                Buffs.Apply(
                    this,
                    new CombatTargetRef(ECombatSide.Enemy, i),
                    buffRef.BuffId,
                    buffRef.Params);
            }
        }
    }

    public void Dispose()
    {
        _teamMaxHealthCoordinator.Dispose();
    }
}