using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Logging;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Run.Reward;
using KemoCard.Mod.Run.Save;

namespace KemoCard.Mod.Run;

public sealed class RunController : BaseController<RunMod>
{
    private readonly RunRewardDistributor _rewardDistributor = new();
    private readonly IContentEffectScriptHost _scriptHost;
    private readonly CombatRuleCatalog _ruleCatalog;
    private RunDto? _battleSnapshot;
    private CombatSimulation? _simulation;
    private RunSaveService? _autoSaveService;

    /// <param name="scriptHost">效果脚本宿主（会话级依赖，由组合根注入；缺省为 Null 实现）。</param>
    /// <param name="ruleCatalog">战斗规则目录；缺省为内置规则集。</param>
    public RunController(
        RunMod model,
        IContentEffectScriptHost? scriptHost = null,
        CombatRuleCatalog? ruleCatalog = null) : base(model)
    {
        _scriptHost = scriptHost ?? new NullContentEffectScriptHost();
        _ruleCatalog = ruleCatalog ?? CombatRuleCatalog.CreateDefault();
    }

    public RunMod State => Model;

    #region 生命周期

    public RunDto CreateRun(string storyId, HostRng rng, IReadOnlyList<CharacterDto> candidates, bool isMultiplayer)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);

        Model.StoryId = storyId;
        Model.RunId = Guid.NewGuid().ToString("N");
        Model.RunSeed = rng.RunSeed;
        Model.IsMultiplayer = isMultiplayer;
        Model.CurrentRing = 1;
        Model.SharedGold = 0;

        if (isMultiplayer)
        {
            for (var i = 0; i < RunConstants.SlotCount; i++)
                Model.PlayerStates[i].SetGold(0);
        }

        if (!isMultiplayer)
        {
            var localController = new PlayerController("local", "Player", isOwner: true);
            Model.AddPlayerController(localController);
            for (var i = 0; i < RunConstants.SlotCount; i++)
                Model.AssignSlotInternal(i, "local");
        }

        Model.Phase = ERunPhase.Event;
        AutoSaveIfSettled();
        return Model.ToDto();
    }

    public RunDto LoadRun(RunDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Model.RestoreFrom(dto);
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
        Model.AddToCharacterPool(character);
        return true;
    }

    public bool RemoveFromCharacterPool(string instanceId)
    {
        return Model.RemoveFromCharacterPool(instanceId);
    }

    public bool SetActiveCharacter(int slotIndex, int poolIndex)
    {
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return false;
        if (poolIndex < 0 || poolIndex >= Model.CharacterPool.Count)
            return false;

        Model.PlayerStates[slotIndex].SetActiveCharacter(Model.CharacterPool[poolIndex]);
        return true;
    }

    public bool UnsetActiveCharacter(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return false;

        Model.PlayerStates[slotIndex].SetActiveCharacter(null);
        return true;
    }

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

    public int GetGold(int? slotIndex = null)
    {
        if (!Model.IsMultiplayer)
            return Model.SharedGold;

        if (slotIndex.HasValue && slotIndex.Value >= 0 && slotIndex.Value < RunConstants.SlotCount)
            return Model.PlayerStates[slotIndex.Value].Gold;

        return 0;
    }

    public bool SpendGold(int slotIndex, int amount)
    {
        if (amount <= 0)
            return true;

        if (!Model.IsMultiplayer)
        {
            if (Model.SharedGold < amount)
                return false;
            Model.SharedGold -= amount;
            return true;
        }

        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return false;
        return Model.PlayerStates[slotIndex].SpendGold(amount);
    }

    public bool AddGold(int slotIndex, int amount)
    {
        if (amount <= 0)
            return false;

        if (!Model.IsMultiplayer)
        {
            Model.SharedGold += amount;
            return true;
        }

        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return false;
        Model.PlayerStates[slotIndex].AddGold(amount);
        return true;
    }

    public bool TransferGold(int fromSlot, int toSlot, int amount)
    {
        if (!Model.IsMultiplayer)
            return false;
        if (fromSlot < 0 || fromSlot >= RunConstants.SlotCount)
            return false;
        if (toSlot < 0 || toSlot >= RunConstants.SlotCount)
            return false;
        if (fromSlot == toSlot)
            return false;
        if (amount <= 0)
            return false;
        if (Model.Phase == ERunPhase.Battle || Model.Phase == ERunPhase.BattleEnd)
            return false;

        if (!Model.PlayerStates[fromSlot].SpendGold(amount))
            return false;
        Model.PlayerStates[toSlot].AddGold(amount);
        return true;
    }

    #endregion

    #region 流程控制

    public void NextRing()
    {
        if (!HasNextRing())
            return;
        Model.CurrentRing++;
        Model.Phase = ERunPhase.Event;
        AutoSaveIfSettled();
    }

    public bool HasNextRing()
    {
        return Model.CurrentRing < Model.MaxRing;
    }

    #endregion

    #region 控制权管理

    public bool AssignSlot(int slotIndex, string playerId)
    {
        if (Model.Phase == ERunPhase.Battle || Model.Phase == ERunPhase.BattleEnd)
            return false;
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return false;
        if (!Model.PlayerControllers.Any(pc => pc.PlayerId == playerId))
            return false;

        Model.AssignSlotInternal(slotIndex, playerId);
        return true;
    }

    public void OnPlayerDisconnected(string playerId)
    {
        if (Model.Phase == ERunPhase.Battle || Model.Phase == ERunPhase.BattleEnd)
        {
            var ownerId = Model.PlayerControllers.FirstOrDefault(pc => pc.IsOwner)?.PlayerId;
            if (playerId == ownerId)
            {
                AbandonRun();
                return;
            }

            if (ownerId != null)
            {
                foreach (var slot in GetSlotsForPlayer(playerId))
                    Model.AssignSlotInternal(slot, ownerId);
            }
        }
        else
        {
            foreach (var slot in GetSlotsForPlayer(playerId))
                Model.AssignSlotInternal(slot, "");
        }
    }

    public bool SwapSlotOwnership(int slotA, int slotB, string initiatorId, string targetId)
    {
        if (Model.Phase == ERunPhase.Battle || Model.Phase == ERunPhase.BattleEnd)
            return false;
        if (slotA < 0 || slotA >= RunConstants.SlotCount)
            return false;
        if (slotB < 0 || slotB >= RunConstants.SlotCount)
            return false;

        var currentA = Model.SlotOwnership.TryGetValue(slotA, out var ownerA) ? ownerA : "";
        var currentB = Model.SlotOwnership.TryGetValue(slotB, out var ownerB) ? ownerB : "";

        if (currentA != initiatorId || currentB != targetId)
            return false;

        Model.AssignSlotInternal(slotA, targetId);
        Model.AssignSlotInternal(slotB, initiatorId);
        return true;
    }

    public bool AllSlotsAssigned()
    {
        return Model.AllSlotsAssigned();
    }

    public IReadOnlyList<int> GetSlotsForPlayer(string playerId)
    {
        var slots = new List<int>();
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            if (Model.SlotOwnership.TryGetValue(i, out var owner) && owner == playerId)
                slots.Add(i);
        }
        return slots;
    }

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

        if (Model.Phase != ERunPhase.Reward)
            throw new InvalidOperationException("只能在Reward阶段进入战斗。");
        if (!AllSlotsAssigned())
            throw new InvalidOperationException("存在未分配控制权的槽位，无法进入战斗。");
        if (!ValidateParty())
            throw new InvalidOperationException("所有槽位必须上阵角色才能进入战斗。");

        var battle = ResolveBattle(definitions, battleId);
        var modId = ResolveOwnerModId(definitions, battle);

        // 快照必须在任何状态变更（含工厂内的角色战斗实例化）之前取。
        _battleSnapshot = Model.ToDto();

        var simulation = CombatSimulationFactory.TryCreate(
            battle,
            BuildParty(),
            definitions,
            rng,
            runSeed,
            runRuleIds: null,
            _scriptHost,
            modId,
            _ruleCatalog,
            out var error);
        if (simulation is null)
            throw new InvalidOperationException(error ?? $"战斗 '{battle.Id}' 创建失败。");

        // 规格 §6.1：必须走 BattleStart 管线（注入技能 → 冻结补满 SharedHp → 每人开局抽满手牌）。
        // 不能以 initialPhase: Player 直接起手，否则被动/修饰技能、补满与开局抽牌全部失效。
        simulation.RunBattleStart();

        _simulation?.Dispose();
        _simulation = simulation;
        Model.Phase = ERunPhase.Battle;
        return simulation;
    }

    /// <summary>
    /// 解析本环要打的战斗定义。环→战斗的映射尚未落地（run-mod-design §5 的环内容后置），
    /// 因此只有在内容里恰好存在一场战斗时才可无歧义推导；否则必须由调用方显式指定，
    /// 绝不退回硬编码的占位敌人。
    /// </summary>
    private static BattleDto ResolveBattle(GameDefinitionRegistry definitions, string? battleId)
    {
        if (!string.IsNullOrWhiteSpace(battleId))
        {
            if (!definitions.Store.TryGetBattle(battleId, out var named))
                throw new InvalidOperationException($"战斗定义 '{battleId}' 不存在。");
            return named;
        }

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

    public void EndBattle(bool won)
    {
        if (Model.Phase != ERunPhase.Battle && Model.Phase != ERunPhase.BattleEnd)
            throw new InvalidOperationException("当前不在战斗中。");

        if (_simulation != null)
        {
            _simulation.Dispose();
            _simulation = null;
        }

        if (won)
        {
            Model.AddBattleRecord(new BattleRecordDto
            {
                BattleId = $"battle_r{Model.CurrentRing}",
                Won = true,
            });

            Model.Phase = ERunPhase.RingEnd;
            AutoSaveIfSettled();
        }
        else
        {
            if (_battleSnapshot != null)
            {
                var instanceLookup = Model.CharacterPool
                    .ToDictionary(c => c.InstanceId, c => c, StringComparer.Ordinal);
                Model.RestoreFrom(_battleSnapshot, instanceLookup);
                _battleSnapshot = null;
            }

            Model.Phase = ERunPhase.Event;
        }
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
        Model.RestoreFrom(dto);
        return true;
    }

    #endregion
}