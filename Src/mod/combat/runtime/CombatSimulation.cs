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
    public int BlockedUnselectedDiscardCount { get; private set; }
    internal void CountBlockedUnselectedDiscard() => BlockedUnselectedDiscardCount++;
    internal Queue<int>? SelectedDiscardSlots { get; set; }
    internal int SelectedDiscardCharacterIndex { get; set; } = -1;
    private readonly ShieldLifecycle _shieldLifecycle;
    internal float CurrentOrbHealingBonus { get; set; }
    private readonly CombatStateMachine _stateMachine;
    private readonly TeamMaxHealthCoordinator _teamMaxHealthCoordinator;
    private readonly List<PlayedCardRecord> _playedThisTurn = [];
    private readonly Dictionary<string, int> _orbsTriggeredThisTurn = new(StringComparer.Ordinal);
    private long _nextQueueSequence = 1;

    /// <summary>统一回合边界，包含幂等收尾、钩子与终局检查。</summary>
    private readonly CombatTurnCoordinator _turnCoordinator;

    public PlayerTeamState PlayerTeam { get; }
    public EnemyTeamState EnemyTeam { get; }
    public CombatRuleEngine Rules { get; }
    public GameDefinitionRegistry Definitions { get; }
    public CardExecutionQueue CardQueue { get; }
    public CombatEffectExecutor EffectExecutor { get; }
    internal EffectExecutionBudget EffectBudget { get; } = new();
    public int RejectedEffectExecutionCount => EffectBudget.RejectedCount;
    public TeamDomainManager DomainManager { get; }
    internal CombatEffectLifecycle EffectLifecycle { get; }
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
    /// 当前结算卡牌适用的连携加成（独立乘算）。
    /// 由状态机在单卡结算区间设置，结算完归零——卡牌上下文之外恒为 0。
    /// </summary>
    public float CurrentChainBonus { get; private set; }

    /// <summary>本回合结算区间内各属性的连携人头数快照（结算开始前一次性定档）；区间之外为空。</summary>
    public IReadOnlyDictionary<EElement, int> CurrentChainCounts => _currentChainCounts;

    /// <summary>当前正在结算的卡牌属性位（0 = 不在单卡结算区间内）；连携条件按它取"这张牌的属性"。</summary>
    public int CurrentChainCardElementFlags { get; private set; }

    /// <summary>
    /// 当前正在结算的卡牌类型（<c>null</c> = 不在单卡结算区间内）。
    /// 供「物理攻击的卡牌攻击次数 +N」这类按卡牌类型生效的加成判定（<c>PhysicalCardAttackCount</c>）。
    /// </summary>
    public KemoCard.Frame.Content.Definitions.ECardType? CurrentCardType { get; private set; }

    /// <summary>当前正在结算卡牌的打出者槽位（-1 = 不在单卡结算区间内）。</summary>
    public int CurrentCardSourceIndex { get; private set; } = -1;

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
        EffectLifecycle = new CombatEffectLifecycle(this);
        EffectLifecycle.AttachHolders();
        EnemyAi = new EnemyAiController(definitions, enemyAiScriptInvoker, modId, runSeed);
        _teamMaxHealthCoordinator = new TeamMaxHealthCoordinator(PlayerTeam);
        _stateMachine = new CombatStateMachine(initialPhase);
        _turnCoordinator = new CombatTurnCoordinator(this);
        _shieldLifecycle = new ShieldLifecycle(this);
    }

    public CombatApplyResult TryApply(ICombatCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _stateMachine.TryApply(this, command);
    }

    public CombatContext CreateContext() => new(this, TurnNumber);

    internal void TransitionTo(ECombatPhase phase)
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

    /// <summary>
    /// 最近一次 <c>DiscardAndRecord</c>（技能动作）<b>实际</b>弃置的张数：请求弃 N 张但可弃池不足时为实际张数，
    /// 一张没弃到记 0。未写入时为 0。
    /// </summary>
    /// <remarks>
    /// 记账语义（2026-09-27「弃 X 张，则下次抽牌 +X」）：<c>DiscardAndRecord</c> 写入
    /// （<see cref="SetLastDiscardCount"/>）；读取方<b>不清账</b>，因此同一账期内可以有多个读取者
    /// （如「按弃牌数补抽」与「按弃牌数扣减行动次数」）看到同一个数值。
    /// 账期与玩家阶段对齐：<c>PlayerPhasePipeline</c> 在每个玩家阶段开始时清一次
    /// （<see cref="ClearLastDiscardCount"/>），主动技在玩家阶段内释放、其弃牌记录因此只在本回合有效，
    /// 不会把上一回合的弃牌数泄漏给下一次读取。
    /// </remarks>
    public int LastDiscardCount { get; private set; }

    /// <summary><c>DiscardAndRecord</c> 专用：登记本次实际弃置的张数（负数按 0 处理）。</summary>
    internal void SetLastDiscardCount(int count) => LastDiscardCount = Math.Max(0, count);

    /// <summary>玩家阶段开始清账：弃牌记录只在本回合内有效（由 <c>PlayerPhasePipeline</c> 调用）。</summary>
    internal void ClearLastDiscardCount() => LastDiscardCount = 0;

    /// <summary>状态机专用：设置/清零当前结算卡牌的连携加成（结算区间之外恒为 0）。</summary>
    internal void SetChainBonus(float bonus) => CurrentChainBonus = bonus;

    /// <summary>状态机专用：登记本回合连携定档快照（结算阶段开始时一次性写入）。</summary>
    internal void SetChainCounts(IReadOnlyDictionary<EElement, int> counts) =>
        _currentChainCounts = counts ?? new Dictionary<EElement, int>();

    /// <summary>状态机专用：登记当前正在结算的卡牌属性位（0 = 离开单卡结算区间）。</summary>
    internal void SetChainCardElementFlags(int elementFlags) => CurrentChainCardElementFlags = elementFlags;

    /// <summary>状态机专用：登记当前正在结算的卡牌类型与打出者（null / -1 = 离开单卡结算区间）。</summary>
    internal void SetCurrentCardContext(
        KemoCard.Frame.Content.Definitions.ECardType? cardType,
        int sourceIndex)
    {
        CurrentCardType = cardType;
        CurrentCardSourceIndex = cardType is null ? -1 : sourceIndex;
    }

    /// <summary>单卡上下文覆盖到结算后钩子；异常退出或嵌套结算后恢复调用方上下文。</summary>
    internal CardContextScope EnterCardContext(ECardType cardType, int sourceIndex, int elementFlags, float chainBonus)
    {
        var scope = new CardContextScope(this, CurrentCardType, CurrentCardSourceIndex, CurrentChainCardElementFlags, CurrentChainBonus);
        SetCurrentCardContext(cardType, sourceIndex);
        SetChainCardElementFlags(elementFlags);
        SetChainBonus(chainBonus);
        return scope;
    }

    internal readonly struct CardContextScope(CombatSimulation simulation, ECardType? cardType, int sourceIndex, int elementFlags, float chainBonus) : IDisposable
    {
        public void Dispose()
        {
            simulation.SetCurrentCardContext(cardType, sourceIndex);
            simulation.SetChainCardElementFlags(elementFlags);
            simulation.SetChainBonus(chainBonus);
        }
    }

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

    /// <summary>当前批次内每个角色被敌方攻击命中的次数（一次敌方技能 = 一个批次）。</summary>
    private readonly Dictionary<int, List<CombatTargetRef?>> _pendingDamagedHits = [];

    /// <summary>
    /// 记一次玩家角色的受击。调用点是伤害统一落点 <c>DamagePipeline.NotifyAfter</c>，
    /// 只在"玩家槽位 + 敌方来源 + 非自我结算"时计入；同一批次里先累计，
    /// 由 <see cref="FlushOnDamagedHits"/> 在批次结束时按次数逐次触发 onDamaged——
    /// 「先结算完全部伤害，再按受击次数回复生命」。
    /// </summary>
    internal void RecordDamagedPlayerHit(int characterIndex, CombatTargetRef? attacker = null)
    {
        if (characterIndex < 0 || characterIndex >= PlayerTeam.Characters.Count)
            return;

        if (!_pendingDamagedHits.TryGetValue(characterIndex, out var hits))
            _pendingDamagedHits[characterIndex] = hits = [];
        hits.Add(attacker);
    }

    /// <summary>批次结束：逐角色按其受击次数触发 onDamaged 钩子并清账（无待处理时零操作）。</summary>
    internal void FlushOnDamagedHits()
    {
        if (_pendingDamagedHits.Count == 0)
            return;

        // 快照后清账：钩子（回血等）自身不会再产生受击，但保持防御性。
        var pending = _pendingDamagedHits.ToArray();
        _pendingDamagedHits.Clear();
        foreach (var (characterIndex, hits) in pending)
            foreach (var attacker in hits)
                Buffs.FireOnDamagedHits(this, characterIndex, 1, attacker);
    }

    #region 充能球触发批次（OrbTriggered 条件）

    /// <summary>批次区间是否有效（只在 onOrbTriggered 钩子求值期间为 true）。</summary>
    private readonly Stack<OrbTriggerBatch?> _orbTriggeredBatches = new();

    private sealed record OrbTriggerBatch(int ElementFlags, IReadOnlySet<string> OrbTypeIds);

    /// <summary>进入触发流程时遮蔽外层批次，避免内层逐球效果误读外层钩子的批次。</summary>
    internal OrbTriggerScope EnterOrbTriggerScope()
    {
        _orbTriggeredBatches.Push(null);
        return new OrbTriggerScope(this);
    }

    internal readonly struct OrbTriggerScope(CombatSimulation simulation) : IDisposable
    {
        public void Dispose() => simulation.EndOrbTriggeredBatch();
    }

    /// <summary>
    /// 开始记录一次充能球触发批次（<c>OrbRuntime.Trigger</c> 在 <c>FireOrbTriggered</c> 之前调用）：
    /// 汇总本次清空队列里所有球的类型 id 与元素位，供 <c>OrbTriggered</c> 条件判断"这次触发里有没有某种球"。
    /// </summary>
    /// <remarks>
    /// 区间只覆盖 onOrbTriggered 钩子的求值：逐球触发效果（<c>orbType.triggerEffects</c>）在批次记录之前执行，
    /// 读不到批次——它们的条件按"这一颗球"的上下文（源 = 产球者）表达，不需要批次信息。
    /// 嵌套触发使用独立批次；内层结束后恢复外层，后续钩子仍读取原批次。
    /// </remarks>
    internal void BeginOrbTriggeredBatch(IReadOnlyCollection<string> orbTypeIds)
    {
        ArgumentNullException.ThrowIfNull(orbTypeIds);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var elements = 0;
        foreach (var orbTypeId in orbTypeIds)
        {
            if (string.IsNullOrWhiteSpace(orbTypeId))
                continue;

            ids.Add(orbTypeId);
            if (Definitions.Store.TryGetOrbType(orbTypeId, out var orbType))
                elements |= (int)orbType.Element;
        }
        _orbTriggeredBatches.Push(new(elements, ids));
    }

    /// <summary>结束批次记录（钩子求值结束后调用）；区间之外 <see cref="OrbBatchMatches"/> 一律 false。</summary>
    internal void EndOrbTriggeredBatch()
    {
        _orbTriggeredBatches.TryPop(out _);
    }

    /// <summary>
    /// 当前触发批次里是否有匹配的充能球（<c>OrbTriggered</c> 条件读它）：
    /// <paramref name="elementMask"/> 为 0 时不筛属性、<paramref name="orbTypeId"/> 为空时不筛球类型；
    /// 两者都给出时须<b>同时</b>命中（"且"）。批次之外（含逐球触发效果区间）恒为 false。
    /// </summary>
    public bool OrbBatchMatches(int elementMask, string? orbTypeId)
    {
        if (!_orbTriggeredBatches.TryPeek(out var batch) || batch is null)
            return false;

        if (elementMask != 0 && (batch.ElementFlags & elementMask) == 0)
            return false;

        return string.IsNullOrWhiteSpace(orbTypeId) || batch.OrbTypeIds.Contains(orbTypeId);
    }

    #endregion

    #region 魔法受击账（TookMagicDamageLastTurn 条件）

    /// <summary>本回合受到魔法伤害的玩家角色槽位。</summary>
    private readonly HashSet<int> _magicDamagedThisTurn = [];

    /// <summary>上一回合受到魔法伤害的玩家角色槽位（回合边界由本回合账滚动而来）。</summary>
    private readonly HashSet<int> _magicDamagedLastTurn = [];

    /// <summary>
    /// 记一次玩家角色的魔法受击。调用点与 onDamaged 记账同处（<c>DamagePipeline.NotifyAfter</c>：
    /// 玩家槽位 + 敌方来源 + 非自我结算），只多一个"伤害维度 = 魔法"的门闩；
    /// 同一角色一回合内多次受击只记一次（条件是布尔语义）。
    /// </summary>
    internal void RecordMagicDamageTaken(int characterIndex)
    {
        if (characterIndex < 0 || characterIndex >= PlayerTeam.Characters.Count)
            return;

        _magicDamagedThisTurn.Add(characterIndex);
    }

    /// <summary>
    /// 回合边界滚动魔法受击账：本回合账转为"上一回合"账，新回合从零累计
    /// （与 <see cref="ResetOrbsTriggeredThisTurn"/> 同一时点，由 <c>CombatTurnCoordinator.BeginNext</c> 调用）。
    /// </summary>
    /// <remarks>
    /// 必须双缓冲：条件语义是"<b>上一回合</b>受到过魔法伤害"，若像球数统计那样单账在回合开始清零，
    /// 上回合的记录会在被读取之前就抹掉，条件永远为假。滚动发生在 <c>Start()</c> 之前，
    /// 因此回合开始的钩子（如爱因斯坦被动2 的 onTurnStart）读到的是上一回合的账。
    /// </remarks>
    internal void RollMagicDamageTurnLedger()
    {
        _magicDamagedLastTurn.Clear();
        _magicDamagedLastTurn.UnionWith(_magicDamagedThisTurn);
        _magicDamagedThisTurn.Clear();
    }

    /// <summary>该槽位角色上一回合是否受到过魔法伤害（<c>TookMagicDamageLastTurn</c> 条件读它）。</summary>
    public bool TookMagicDamageLastTurn(int characterIndex) =>
        characterIndex >= 0 &&
        characterIndex < PlayerTeam.Characters.Count &&
        _magicDamagedLastTurn.Contains(characterIndex);

    #endregion

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

        // 换波即切到下一个回合（2026-09-26 定案）：新波登场后本回合直接结束——角色解除已行动、
        // 回合数 +1、跑完整回合开始管线（能量 / S / 抽牌），玩家在下一回合行动。
        // 回合结束管线在此补跑（2026-09-26 修正）：敌方阶段清波时 ExecuteEnemyPhase 已跑过，
        // ResolveTurnEnd 幂等；玩家阶段 / 卡牌执行阶段清波时本回合尚未结算，必须**在生成新敌人之前**
        // 补跑（否则回合结束产出的球会砸到新波），两条路径不会重复结算。
        // `TurnsIntoWave` 保持换波时的 0（新波第一回合，与开战首回合同口径），由下一次回合结束再递增。
        ResolveTurnEnd();
        if (PlayerTeam.IsDefeated)
        {
            // 回合结束效果（充能球触发 / onTurnEnd）可能反噬队伍：同归于尽仍判负（规格 §1.3）。
            TransitionTo(ECombatPhase.Defeat);
            return;
        }

        TransitionTo(ECombatPhase.WaveTransition);
        CurrentWaveIndex++;
        var enemies = CombatSimulationFactory.SpawnWaveEnemies(
            Battle.Waves[CurrentWaveIndex],
            Definitions,
            out var error);
        if (enemies is null)
            throw new InvalidOperationException(error ?? "下一波敌人生成失败。");

        EnemyTeam.ReplaceEnemies(enemies);
        EffectLifecycle.AttachHolders();
        TurnsIntoWave = 0;
        Presentation.Emit(new WaveStartedEvent(CurrentWaveIndex));
        // 新波次的敌人是全新的实例（buff 容器为空）：必须重新挂载内容声明的开战 buff，
        // 否则 EnemyDto.buffRefs 只在第一波生效。顺序与 BattleStart 一致——先挂 buff 再发阶层钩子。
        ApplyEnemyInitialBuffs();
        // 阶层开始钩子（波内每 N 回合的计时基准同时归零）。
        Buffs.FireWaveStart(this);

        foreach (var character in PlayerTeam.Characters)
            character.SetHasActed(false);
        BeginNextTurn(incrementTurnsIntoWave: false);
    }

    internal void ResolveTurnEnd() => _turnCoordinator.End();
    internal void BeginNextTurn(bool incrementTurnsIntoWave) => _turnCoordinator.BeginNext(incrementTurnsIntoWave);
    internal void RunTurnStart() => _turnCoordinator.Start();
    /// <summary>
    /// 把每个敌人 <c>buffRefs</c> 声明的 buff 挂到它自己身上（与玩家侧开战被动注入对称）。
    /// 未找到敌人定义时软失败跳过。BattleStart 与每次换波都要调用一次。
    /// </summary>
    internal void ApplyEnemyInitialBuffs()
    {
        for (var i = 0; i < EnemyTeam.Enemies.Count; i++)
        {
            var enemy = EnemyTeam.Enemies[i];
            if (Definitions.Store.TryGetEnemy(enemy.DefinitionId, out var definition))
            {
                foreach (var buffRef in definition.BuffRefs)
                    Buffs.Apply(this, new CombatTargetRef(ECombatSide.Enemy, i), buffRef.BuffId, buffRef.Params);
            }
            Buffs.FireEnemyEntered(this, i);
        }
        DomainManager.RefreshDomainBuffs();
    }

    public void Dispose()
    {
        _teamMaxHealthCoordinator.Dispose();
        _shieldLifecycle.Dispose();
    }
}