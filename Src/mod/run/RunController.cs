using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Run.Reward;
using KemoCard.Mod.Run.Save;

namespace KemoCard.Mod.Run;

public sealed class RunController : BaseController<RunMod>
{
    private readonly RunRewardDistributor _rewardDistributor = new();
    private RunDto? _battleSnapshot;
    private CombatSimulation? _simulation;

    public RunController(RunMod model) : base(model)
    {
    }

    public RunMod State => Model;

    #region 生命周期

    public RunDto CreateRun(HostRng rng, IReadOnlyList<CharacterDto> candidates, bool isMultiplayer)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(candidates);

        Model.RunId = Guid.NewGuid().ToString("N");
        Model.RunSeed = rng.NextInt(1, int.MaxValue);
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
        int runSeed)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(rng);

        if (Model.Phase != ERunPhase.Reward)
            throw new InvalidOperationException("只能在Reward阶段进入战斗。");
        if (!AllSlotsAssigned())
            throw new InvalidOperationException("存在未分配控制权的槽位，无法进入战斗。");
        if (!ValidateParty())
            throw new InvalidOperationException("所有槽位必须上阵角色才能进入战斗。");

        _battleSnapshot = Model.ToDto();

        var activeParty = Model.ActiveParty;
        var battleCharacters = new List<CharacterBattleInstance>();
        for (var i = 0; i < activeParty.Length; i++)
        {
            var source = activeParty[i];
            if (source == null)
                continue;

            var battleInstance = CharacterBattleInstance.TryCreate(source, definitions, rng, out var error);
            if (battleInstance == null)
                throw new InvalidOperationException(error ?? "角色战斗实例创建失败。");
            battleCharacters.Add(battleInstance);
        }

        if (battleCharacters.Count != RunConstants.SlotCount)
            throw new InvalidOperationException("队伍必须包含 4 名角色。");

        var sharedMaxHp = battleCharacters.Sum(c => c.Asc.GetCurrentValue(Frame.Gas.AttributeIds.MaxHealth));
        var playerTeam = new PlayerTeamState(battleCharacters, (int)MathF.Round(sharedMaxHp));
        var enemy = new EnemyUnit("enemy-default", "slime", new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [Frame.Gas.AttributeIds.MaxHealth] = 10f,
        });
        var enemyTeam = new EnemyTeamState([enemy]);
        var ruleEngine = new CombatRuleEngine([]);

        _simulation = new CombatSimulation(
            playerTeam,
            enemyTeam,
            ruleEngine,
            definitions,
            initialPhase: ECombatPhase.Player,
            runSeed: runSeed);

        Model.Phase = ERunPhase.Battle;
        return _simulation;
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

    public void Save(RunSaveService saveService)
    {
        ArgumentNullException.ThrowIfNull(saveService);
        saveService.Save(Model.ToDto());
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