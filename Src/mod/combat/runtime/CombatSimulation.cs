using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.Ai;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.NormalAttack;
using KemoCard.Mod.Combat.Orbs;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Combat.Gas;

namespace KemoCard.Mod.Combat.Runtime;

public sealed class CombatSimulation : IDisposable
{
    private readonly CombatStateMachine _stateMachine;
    private readonly TeamMaxHealthCoordinator _teamMaxHealthCoordinator;
    private readonly List<PlayedCardRecord> _playedThisTurn = [];
    private readonly Dictionary<string, int> _orbsTriggeredThisTurn = new(StringComparer.Ordinal);
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

    /// <summary>
    /// 表现事件日志（规格 §16）：逻辑只在编排层 <c>Emit</c> 值事件，界面 <c>Drain</c> 后自行安排动画。
    /// 始终存在（不是构造参数），逻辑层不知道是否有人消费。
    /// </summary>
    public CombatPresentationLog Presentation { get; } = new();

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

    /// <summary>本回合结算区间内各属性的连携人头数快照（结算开始前一次性定档）；区间之外为空。</summary>
    public IReadOnlyDictionary<EElement, int> CurrentChainCounts => _currentChainCounts;

    /// <summary>当前正在结算的卡牌属性位（0 = 不在单卡结算区间内）；连携条件按它取"这张牌的属性"。</summary>
    public int CurrentChainCardElementFlags { get; private set; }

    private IReadOnlyDictionary<EElement, int> _currentChainCounts =
        new Dictionary<EElement, int>();

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

    public void TransitionTo(ECombatPhase phase)
    {
        var previous = _stateMachine.Phase;
        _stateMachine.TransitionTo(phase);
        if (previous != phase)
            Presentation.Emit(new PhaseChangedEvent(previous, phase, TurnNumber));
    }

    public void AdvancePhase() => _stateMachine.Advance(this);

    /// <summary>
    /// 宿主（战斗界面）在一条玩家指令成功后调用：把不需要玩家输入的相位（卡牌执行 → 敌方）一路推进，
    /// 直到回到玩家阶段或战斗结束。表现事件在此期间持续记入 <see cref="Presentation"/>，界面事后统一播放。
    /// </summary>
    public void AdvanceAutomaticPhases()
    {
        // 每个自动相位各推进一次即回到 Player / 终局；守卫只是防御状态机异常时的死循环。
        var guard = 0;
        while (Phase is ECombatPhase.CardExecution or ECombatPhase.Enemy && guard++ < 8)
            AdvancePhase();
    }

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

    /// <summary>状态机专用：登记本回合连携定档快照（结算阶段开始时一次性写入）。</summary>
    internal void SetChainCounts(IReadOnlyDictionary<EElement, int> counts) =>
        _currentChainCounts = counts ?? new Dictionary<EElement, int>();

    /// <summary>状态机专用：登记当前正在结算的卡牌属性位（0 = 离开单卡结算区间）。</summary>
    internal void SetChainCardElementFlags(int elementFlags) => CurrentChainCardElementFlags = elementFlags;

    /// <summary>
    /// 连携人头数查询（战斗条件 <c>ChainTierAtLeast</c> 读它）：
    /// <paramref name="elementFlags"/> 为 0 时用**当前结算卡**的属性位；
    /// 取这些属性里参与人数（不同角色数）的<b>最大值</b>，多属性卡取最优。
    /// 结算区间之外（或该属性无人参与）返回 0。
    /// </summary>
    public int CountChainParticipants(int elementFlags)
    {
        var flags = elementFlags != 0 ? elementFlags : CurrentChainCardElementFlags;
        if (flags == 0)
            return 0;

        var best = 0;
        foreach (var element in Enum.GetValues<EElement>())
        {
            if (element == EElement.None || (flags & (int)element) == 0)
                continue;

            if (_currentChainCounts.TryGetValue(element, out var count))
                best = Math.Max(best, count);
        }

        return best;
    }

    internal void CountBlockedMidDraw() => BlockedMidDrawCount++;

    /// <summary>本回合已打出的卡牌登记（充能球回合结束统计口径；含空放——牌已离手即算打出）。</summary>
    internal void RecordPlayedCard(string cardId, int characterIndex) =>
        _playedThisTurn.Add(new PlayedCardRecord(cardId, characterIndex));

    /// <summary>
    /// 本回合出牌登记的只读视图（<c>CardPlayedThisTurn</c> 条件与回合内被动读它）。
    /// 生命周期：结算阶段累计 → 回合结束产球时由 <see cref="TakePlayedThisTurn"/> 取走并清空。
    /// </summary>
    public IReadOnlyList<PlayedCardRecord> PlayedThisTurn => _playedThisTurn;

    /// <summary>
    /// 本回合该角色打出的卡牌张数：只统计属性与 <paramref name="elementFlags"/> 有交集的卡
    /// （<paramref name="elementFlags"/> 为 0 时不筛属性）。含空放——牌离开手牌即算打出。
    /// </summary>
    public int CountCardsPlayedThisTurn(int characterIndex, int elementFlags)
    {
        var count = 0;
        foreach (var record in _playedThisTurn)
        {
            if (record.CharacterIndex != characterIndex)
                continue;

            if (elementFlags == 0)
            {
                count++;
                continue;
            }

            if (Definitions.Store.TryGetCard(record.CardId, out var card) &&
                (card.Element & elementFlags) != 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>取走本回合出牌记录并清空（每回合结束产出充能球时调用一次）。</summary>
    internal IReadOnlyList<PlayedCardRecord> TakePlayedThisTurn()
    {
        if (_playedThisTurn.Count == 0)
            return [];

        var records = _playedThisTurn.ToArray();
        _playedThisTurn.Clear();
        return records;
    }

    /// <summary>
    /// 回合开始：清空"本回合已触发充能球"统计。
    /// </summary>
    /// <remarks>
    /// 账期必须与<b>回合边界</b>对齐。这个统计曾经挂在 <see cref="TakePlayedThisTurn"/>（回合结束产球时）
    /// 一起清，但产球会即时触发并再次记账，于是"上一回合结束产出的球"被算进了下一回合——
    /// 于是「本回合每触发 1 个绿球 +100% 魔攻」会凭空多算。改到回合开始清，语义回到
    /// "本回合内触发的球"；回合结束产出的球发生在全部卡牌结算之后，本就不该归给任何一回合的卡牌。
    /// </remarks>
    internal void ResetOrbsTriggeredThisTurn() => _orbsTriggeredThisTurn.Clear();

    /// <summary>登记本次触发结算掉的充能球（按类型计数，回合开始清账）。</summary>
    internal void RecordOrbsTriggered(IReadOnlyDictionary<string, int> cleared)
    {
        ArgumentNullException.ThrowIfNull(cleared);
        foreach (var (orbTypeId, count) in cleared)
            _orbsTriggeredThisTurn[orbTypeId] = _orbsTriggeredThisTurn.GetValueOrDefault(orbTypeId) + count;
    }

    /// <summary>
    /// 本回合已触发的、元素命中 <paramref name="elementMask"/> 的充能球总数
    /// （掩码 0 = 全部球，含物理/魔法球）。「本回合每触发 1 个绿球 → 增伤」这类效果读它。
    /// </summary>
    public int CountOrbsTriggeredThisTurn(int elementMask)
    {
        var count = 0;
        foreach (var (orbTypeId, triggered) in _orbsTriggeredThisTurn)
        {
            if (elementMask == 0)
            {
                count += triggered;
                continue;
            }

            if (Definitions.Store.TryGetOrbType(orbTypeId, out var orbType) &&
                ((int)orbType.Element & elementMask) != 0)
            {
                count += triggered;
            }
        }

        return count;
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
        Presentation.Emit(new WaveStartedEvent(CurrentWaveIndex));
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