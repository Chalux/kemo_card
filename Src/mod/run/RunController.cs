using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Logging;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Run.Events;
using KemoCard.Mod.Run.Save;

namespace KemoCard.Mod.Run;

public sealed class RunController : BaseController<RunMod>
{
    private readonly IContentEffectScriptHost _scriptHost;
    private readonly CombatRuleCatalog _ruleCatalog;
    private readonly Func<string, CharacterDto?>? _characterDefinitionResolver;
    private RunDto? _battleSnapshot;
    private CombatSimulation? _simulation;
    private RunSaveService? _autoSaveService;


    /// <param name="scriptHost">效果脚本宿主（会话级依赖，由组合根注入；缺省为 Null 实现）。</param>
    /// <param name="ruleCatalog">战斗规则目录；缺省为内置规则集。</param>
    /// <param name="characterDefinitionResolver">
    /// 角色定义解析（读档时按 definitionId 取回完整定义；缺省退回存档内的最小快照）。
    /// 由组合根提供，Run 层不直接访问内容注册表。
    /// </param>
    public RunController(
        RunMod model,
        IContentEffectScriptHost? scriptHost = null,
        CombatRuleCatalog? ruleCatalog = null,
        Func<string, CharacterDto?>? characterDefinitionResolver = null) : base(model)
    {
        _scriptHost = scriptHost ?? new NullContentEffectScriptHost();
        _ruleCatalog = ruleCatalog ?? CombatRuleCatalog.CreateDefault();
        _characterDefinitionResolver = characterDefinitionResolver;
    }

    public RunMod State => Model;
    public void NotifyAdventureChanged()
    {
        Model.NotifyRunAdventureChanged(new RunAdventureChangedPayload());
        AutoSaveIfSettled();
    }

    /// <summary>当前战斗模拟器（未开战为 null）；调试检查器只读使用。</summary>
    public CombatSimulation? Simulation => _simulation;

    #region 生命周期

    public RunDto CreateRun(string storyId, HostRng rng, IReadOnlyList<CharacterDto> candidates)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);

        Model.StoryId = storyId;
        Model.RunId = Guid.NewGuid().ToString("N");
        Model.RunSeed = rng.RunSeed;
        Model.CurrentRing = 1;
        Model.SharedGold = 0;
        Model.Items.Restore([], []);
        Model.Map = null;
        Model.ReservedEventIds.Clear();

        Model.Phase = ERunPhase.Event;
        AutoSaveIfSettled();
        return Model.ToDto();
    }

    public RunDto LoadRun(RunDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Model.RestoreFrom(dto, definitionResolver: _characterDefinitionResolver);
        return Model.ToDto();
    }

    public RunDto AbandonRun()
    {
        if (_simulation != null)
        {
            _simulation.Dispose();
            _simulation = null;
        }

        Model.Phase = ERunPhase.Finished;
        return Model.ToDto();
    }

    #endregion

    #region 队伍管理

    public bool AddToCharacterPool(CharacterInstance character)
    {
        ArgumentNullException.ThrowIfNull(character);

        if (Model.CharacterPool.Any(existing =>
                string.Equals(existing.DefinitionId, character.DefinitionId, StringComparison.Ordinal)))
        {
            return false;
        }

        Model.AddToCharacterPool(character);
        return true;
    }

    public bool RemoveFromCharacterPool(string instanceId)
    {
        return Model.RemoveFromCharacterPool(instanceId);
    }

    /// <summary>
    /// 战斗期间锁定/解锁全部角色卡组（总规格 §4.5.5 换人门闩的卡组侧）：
    /// 战斗内不允许改卡组，避免"战斗中临时换构筑"绕过开战快照。
    /// </summary>
    private void SetDecksLocked(bool locked)
    {
        foreach (var character in Model.CharacterPool)
            character.SetDeckLocked(locked);
    }

    public bool SetActiveCharacter(int slotIndex, int poolIndex)
    {
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return false;
        if (poolIndex < 0 || poolIndex >= Model.CharacterPool.Count)
            return false;

        var previous = Model.PlayerStates[slotIndex].ActiveCharacter?.InstanceId;
        var character = Model.CharacterPool[poolIndex];
        Model.PlayerStates[slotIndex].SetActiveCharacter(character);
        NotifyCharacterAssigned(slotIndex, previous, character.InstanceId);
        return true;
    }

    public bool UnsetActiveCharacter(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return false;

        var previous = Model.PlayerStates[slotIndex].ActiveCharacter?.InstanceId;
        Model.PlayerStates[slotIndex].SetActiveCharacter(null);
        NotifyCharacterAssigned(slotIndex, previous, currentInstanceId: null);
        return true;
    }

    /// <summary>
    /// 广播「某角色的卡组发生变更」。
    /// </summary>
    /// <remarks>
    /// 卡组编辑入口不在本类（<c>CharacterInstance.TryEditDeck</c> 等由队伍编辑与调试面板直接调用），
    /// 因此由写入方在写入成功后显式广播，保证各视图只依赖总线、不互相引用。
    /// </remarks>
    public void NotifyDeckChanged(string? instanceId, int deckIndex) =>
        Model.NotifyRunDeckChanged(new RunDeckChangedPayload
        {
            InstanceId = instanceId,
            DeckIndex = deckIndex,
        });

    private void NotifyCharacterAssigned(int slotIndex, string? previousInstanceId, string? currentInstanceId) =>
        Model.NotifyRunCharacterAssigned(new RunCharacterAssignedPayload
        {
            SlotIndex = slotIndex,
            PreviousInstanceId = previousInstanceId,
            CurrentInstanceId = currentInstanceId,
        });

    public bool ValidateParty()
    {
        return Model.ValidateParty();
    }

    public bool AddCard(string cardId)
    {
        Model.AddCard(cardId);
        return true;
    }

    public bool RemoveCard(string cardId)
    {
        return Model.RemoveCard(cardId);
    }

    #endregion

    #region 金币

    public int GetGold() => Model.SharedGold;

    public bool SpendGold(int amount)
    {
        if (amount <= 0)
            return true;
        if (Model.SharedGold < amount)
            return false;

        Model.SharedGold -= amount;
        return true;
    }

    public bool AddGold(int amount)
    {
        if (amount <= 0 || (long)Model.SharedGold + amount > int.MaxValue)
            return false;

        Model.SharedGold += amount;
        return true;
    }

    #endregion

    #region 流程控制

    /// <summary>
    /// 进入 / 回到探索阶段（地图节点切换、事件结算）。
    /// </summary>
    /// <remarks>
    /// 战斗、环结算（应走 <see cref="NextRing"/>）与已结束的 Run 不允许经此回到探索；
    /// 战败回滚由 <see cref="EndBattle"/> 自己负责，调试面板则显式走 <see cref="ForceSetPhase"/>。
    /// </remarks>
    /// <returns>是否完成了阶段写入。</returns>
    public bool EnterExploration()
    {
        if (Model.Phase is ERunPhase.Battle or ERunPhase.BattleEnd
            or ERunPhase.RingEnd or ERunPhase.Finished)
        {
            return false;
        }

        Model.Phase = ERunPhase.Event;
        return true;
    }

    /// <summary>
    /// 环结算后进入下一环。仅允许在 <see cref="ERunPhase.RingEnd"/> 且仍有下一环时推进。
    /// </summary>
    /// <returns>是否推进到下一环。</returns>
    public bool NextRing()
    {
        if (Model.Phase != ERunPhase.RingEnd || !HasNextRing())
        {
            return false;
        }

        Model.CurrentRing++;
        Model.Phase = ERunPhase.Event;
        AutoSaveIfSettled();
        return true;
    }

    public bool HasNextRing()
    {
        return Model.CurrentRing < Model.MaxRing;
    }

    /// <summary>
    /// 强制设置阶段：仅供调试面板 / 读档恢复等明确需要绕过流程前置的调用方。
    /// 正常流程必须走 <see cref="EnterExploration"/> / <see cref="StartBattle"/> /
    /// <see cref="EndBattle"/> / <see cref="NextRing"/> / <see cref="AbandonRun"/>。
    /// </summary>
    internal void ForceSetPhase(ERunPhase phase) => Model.Phase = phase;

    #endregion

    #region 战斗管理

    public CombatSimulation StartBattle(
        GameDefinitionRegistry definitions,
        HostRng rng,
        int runSeed,
        string? battleId = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(rng);

        if (_simulation is not null)
            throw new InvalidOperationException("已有战斗进行中，不能重复进入战斗。");

        // Event 是正常探索入口；Init / Reward 保留给旧存档 / 调试前置的兼容路径。
        if (Model.Phase is not (ERunPhase.Init or ERunPhase.Event or ERunPhase.Reward))
            throw new InvalidOperationException("只能在初始化、探索或奖励阶段进入战斗。");
        if (Model.BattleHistory.Any(record => record.Won && (record.Ring == Model.CurrentRing ||
            (record.Ring == 0 && record.BattleId == $"battle_r{Model.CurrentRing}"))))
            throw new InvalidOperationException("本环战斗已经完成，请进入下一环。");
        if (!ValidateParty())
            throw new InvalidOperationException("所有槽位必须上阵角色才能进入战斗。");

        var battle = ResolveBattle(definitions, battleId, runSeed);
        var modId = ResolveOwnerModId(definitions, battle);

        // 快照必须在任何状态变更（含工厂内的角色战斗实例化）之前取。
        // 失败时要还原旧快照：调试面板可能在已有战斗进行中再次尝试开战，
        // 把正在进行的战斗的回滚目标换成"战斗中"快照会让后续战败回滚不完整。
        var previousSnapshot = _battleSnapshot;
        _battleSnapshot = Model.ToDto();

        CombatSimulation? simulation = null;
        try
        {
            simulation = CombatSimulationFactory.TryCreate(
                battle,
                BuildParty(),
                definitions,
                rng,
                runSeed,
                runRuleIds: null,
                _scriptHost,
                modId,
                _ruleCatalog,
                out var error,
                initialBuffs: BuildPassiveBuffs(),
                obtainedCardIds: Model.CardCollection);
            if (simulation is null)
                throw new InvalidOperationException(error ?? $"战斗 '{battle.Id}' 创建失败。");

            // 规格 §6.1：必须走 BattleStart 管线（注入技能 → 冻结补满 SharedHp → 每人开局抽满手牌）。
            // 不能以 initialPhase: Player 直接起手，否则被动/修饰技能、补满与开局抽牌全部失效。
            simulation.Items = Model.Items;
            simulation.RunBattleStart();
        }
        catch
        {
            _battleSnapshot = previousSnapshot;
            simulation?.Dispose();
            throw;
        }

        _simulation?.Dispose();
        _simulation = simulation;
        Model.Phase = ERunPhase.Battle;
        SetDecksLocked(true);
        return simulation;
    }

    /// <summary>
    /// 普通入口从故事的本环池抽取；显式 battleId 保留为调试入口。
    /// 旧内容未配置故事池时，仍允许唯一战斗的无歧义推导。
    /// </summary>
    private BattleDto ResolveBattle(GameDefinitionRegistry definitions, string? battleId, int runSeed)
    {
        if (!string.IsNullOrWhiteSpace(battleId))
        {
            if (!definitions.Store.TryGetBattle(battleId, out var named))
                throw new InvalidOperationException($"战斗定义 '{battleId}' 不存在。");
            return named;
        }

        if (definitions.Store.TryGetStory(Model.StoryId, out var story) && story.RingBattlePools.Count > 0)
            return RingBattleSelector.Select(definitions, Model.StoryId, Model.CurrentRing, runSeed);

        var battles = definitions.Store.Battles;
        if (battles.Count == 1)
            return battles.Values.First();

        throw new InvalidOperationException(
            battles.Count == 0
                ? "内容中没有战斗定义，无法进入战斗。"
                : $"内容包含 {battles.Count} 场战斗，无法推导本环应进入哪一场；请显式传入 battleId。");
    }

    /// <summary>效果脚本按 mod 隔离，modId 取自战斗定义的归属 mod。</summary>
    private static string ResolveOwnerModId(GameDefinitionRegistry definitions, BattleDto battle)
    {
        if (definitions.TryGetOwnerModId(EContentCategory.Battle, battle.Id, out var modId) &&
            !string.IsNullOrWhiteSpace(modId))
        {
            return modId;
        }

        throw new InvalidOperationException($"无法确定战斗 '{battle.Id}' 的归属 mod，脚本效果无法解析。");
    }

    private IReadOnlyList<CharacterInstance> BuildParty()
    {
        var activeParty = Model.ActiveParty;
        var party = new List<CharacterInstance>(activeParty.Length);
        foreach (var character in activeParty)
        {
            if (character is null)
                throw new InvalidOperationException("存在未上阵的槽位，无法进入战斗。");
            party.Add(character);
        }

        return party;
    }

    private List<BattleStartBuffEntry> BuildPassiveBuffs()
    {
        var entries = new List<BattleStartBuffEntry>();
        var party = Model.ActiveParty;
        for (var i = 0; i < party.Length; i++)
        {
            var character = party[i];
            if (character?.Definition is null)
                continue;

            foreach (var passive in character.Definition.Passives)
            {
                entries.Add(new BattleStartBuffEntry(i, passive.BuffId, passive.Params));
            }
        }

        return entries;
    }

    /// <summary>
    /// 结束战斗。胜利 → RingEnd；失败 → 有快照则回滚到 Event。
    /// </summary>
    /// <returns>失败时是否实际执行了快照回滚（调试面板据此生成准确提示）。</returns>
    public bool EndBattle(bool won)
    {
        if (Model.Phase != ERunPhase.Battle && Model.Phase != ERunPhase.BattleEnd)
            throw new InvalidOperationException("当前不在战斗中。");

        // 战斗结束解除卡组锁（战斗内不允许改卡组，规格 §4.5.5 的换人门闩同理）。
        SetDecksLocked(false);

        var completedBattleId = _simulation?.Battle?.Id ?? $"battle_r{Model.CurrentRing}";
        if (_simulation != null)
        {
            _simulation.Dispose();
            _simulation = null;
        }

        if (won)
        {
            Model.AddBattleRecord(new BattleRecordDto
            {
                BattleId = completedBattleId,
                Ring = Model.CurrentRing,
                Won = true,
            });

            // 胜利后没有可回滚的战斗了；保留旧快照会让后续失败的 StartBattle 把 previousSnapshot
            // 指向更早的环，进而在调试/异常路径回滚时倒退进度。
            _battleSnapshot = null;
            Model.Phase = ERunPhase.RingEnd;
            AutoSaveIfSettled();
            return false;
        }

        var rolledBack = false;
        if (_battleSnapshot != null)
        {
            var instanceLookup = Model.CharacterPool
                .ToDictionary(c => c.InstanceId, c => c, StringComparer.Ordinal);
            // 快照可能是旧流程的 Reward 阶段；回滚后 Run 只能处于 Event，
            // 归一后再 RestoreFrom，避免先广播 Reward 再广播 Event 的幽灵阶段。
            Model.RestoreFrom(
                _battleSnapshot with { Phase = ERunPhase.Event },
                instanceLookup,
                _characterDefinitionResolver);
            _battleSnapshot = null;
            rolledBack = true;
        }

        Model.Phase = ERunPhase.Event;
        return rolledBack;
    }

    #endregion

    #region 持久化

    public void EnableAutoSave(RunSaveService saveService)
    {
        ArgumentNullException.ThrowIfNull(saveService);
        _autoSaveService = saveService;
    }

    /// <summary>
    /// 阶段切换后的自动保存；战斗中（Battle/BattleEnd）不落盘。
    /// </summary>
    private void AutoSaveIfSettled()
    {
        if (_autoSaveService == null)
        {
            return;
        }

        if (Model.Phase == ERunPhase.Battle || Model.Phase == ERunPhase.BattleEnd)
        {
            return;
        }

        Save(_autoSaveService);
    }

    /// <summary>
    /// 落盘当前 Run。
    /// </summary>
    /// <returns><c>false</c> 表示写盘失败（磁盘满 / 文件被占用等），调用方应提示用户。</returns>
    public bool Save(RunSaveService saveService)
    {
        ArgumentNullException.ThrowIfNull(saveService);
        var saved = saveService.Save(Model.ToDto());
        if (!saved)
        {
            AppLog.Warning($"Run '{Model.RunId}' 存档写入失败。", "RunSave");
        }

        return saved;
    }

    public bool TryLoad(RunSaveService saveService, out RunDto dto)
    {
        ArgumentNullException.ThrowIfNull(saveService);
        var loaded = saveService.LoadOrDefault();
        if (string.IsNullOrEmpty(loaded.RunId))
        {
            dto = new RunDto();
            return false;
        }

        dto = loaded;
        Model.RestoreFrom(dto, definitionResolver: _characterDefinitionResolver);
        return true;
    }

    #endregion
}