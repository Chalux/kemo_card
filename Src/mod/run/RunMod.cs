using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.UI;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run.Ui;

namespace KemoCard.Mod.Run;

public sealed partial class RunMod : BaseMod
{
    private readonly List<CharacterInstance> _characterPool = [];
    private readonly HashSet<string> _cardCollection = new(StringComparer.Ordinal);
    private readonly List<PlayerController> _playerControllers = [];
    private readonly Dictionary<int, string> _slotOwnership = new();
    private readonly List<BattleRecordDto> _battleHistory = [];

    public RunMod() : base("run")
    {
        RunId = Guid.NewGuid().ToString("N");
        Phase = ERunPhase.Event;
        CurrentRing = 1;
        MaxRing = RunConstants.DefaultMaxRings;
    }

    public string RunId { get; set; } = "";
    public string StoryId { get; set; } = "";
    public int CurrentRing { get; set; }
    public int MaxRing { get; set; }
    public ERunPhase Phase { get; set; }
    public int RunSeed { get; set; }
    public bool IsMultiplayer { get; set; }

    public IReadOnlyList<CharacterInstance> CharacterPool => _characterPool;
    public IReadOnlySet<string> CardCollection => _cardCollection;
    public int SharedGold { get; set; }
    public IReadOnlyList<PlayerController> PlayerControllers => _playerControllers;
    public IReadOnlyDictionary<int, string> SlotOwnership => _slotOwnership;
    public IReadOnlyList<BattleRecordDto> BattleHistory => _battleHistory;

    public PlayerRunState[] PlayerStates { get; } = Enumerable.Range(0, RunConstants.SlotCount)
        .Select(_ => new PlayerRunState())
        .ToArray();

    public CharacterInstance?[] ActiveParty => PlayerStates.Select(ps => ps.ActiveCharacter).ToArray();

    public void AddToCharacterPool(CharacterInstance character)
    {
        ArgumentNullException.ThrowIfNull(character);
        _characterPool.Add(character);
    }

    public bool RemoveFromCharacterPool(string instanceId)
    {
        var index = _characterPool.FindIndex(c => c.InstanceId == instanceId);
        if (index < 0)
            return false;
        foreach (var state in PlayerStates)
        {
            if (ReferenceEquals(state.ActiveCharacter, _characterPool[index]))
                state.SetActiveCharacter(null);
        }
        _characterPool.RemoveAt(index);
        return true;
    }

    public bool CanUseCard(string cardId)
    {
        return _cardCollection.Contains(cardId);
    }

    public void AddCard(string cardId)
    {
        _cardCollection.Add(cardId);
    }

    public bool RemoveCard(string cardId)
    {
        return _cardCollection.Remove(cardId);
    }

    public bool ValidateParty()
    {
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            if (PlayerStates[i].ActiveCharacter == null)
                return false;
        }
        return true;
    }

    public void AddPlayerController(PlayerController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _playerControllers.Add(controller);
    }

    public void AssignSlotInternal(int slotIndex, string playerId)
    {
        _slotOwnership[slotIndex] = playerId;
    }

    public bool AllSlotsAssigned()
    {
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            if (!_slotOwnership.TryGetValue(i, out var playerId) || string.IsNullOrEmpty(playerId))
                return false;
        }
        return true;
    }

    public void AddBattleRecord(BattleRecordDto record)
    {
        _battleHistory.Add(record);
    }

    /// <summary>
    /// 声明式注册 run 模块所有 UI。
    /// </summary>
    public static IEnumerable<UIRegistration> GetUIRegistrations()
    {
        yield return UIRegistration.Dialog(RunUiIds.StorySelect, "Src/mod/run/Ui");
        yield return UIRegistration.Window(RunUiIds.RunMain, "Src/mod/run/Ui");
    }

    public static void RegisterUi(UIRuntimeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (var reg in GetUIRegistrations())
        {
            registry.Register(reg.ToRuntimeEntry());
        }
    }

    public RunDto ToDto()
    {
        var characterPoolDtos = _characterPool.Select(c => new CharacterPoolEntryDto
        {
            DefinitionId = c.DefinitionId,
            InstanceId = c.InstanceId,
            DefinitionCardIds = c.Definition?.Cards.ToList() ?? [],
            Decks = c.Decks.Select(d => new DeckSnapshotDto { CardIds = d.CardIds.ToList() }).ToList(),
            CurrentDeckIndex = c.CurrentDeckIndex,
        }).ToList();

        var playerStateDtos = new List<PlayerRunStateDto>();
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            var state = PlayerStates[i];
            var dto = state.ToDto();
            if (state.ActiveCharacter != null)
            {
                var poolIndex = _characterPool.IndexOf(state.ActiveCharacter);
                dto = dto with { ActiveCharacterIndex = poolIndex >= 0 ? poolIndex : null };
            }
            playerStateDtos.Add(dto);
        }

        return new RunDto
        {
            RunId = RunId,
            StoryId = StoryId,
            SchemaVersion = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CurrentRing = CurrentRing,
            MaxRing = MaxRing,
            Phase = Phase,
            RunSeed = RunSeed,
            IsMultiplayer = IsMultiplayer,
            PlayerControllers = _playerControllers.Select(pc => pc.ToDto()).ToList(),
            SlotOwnership = new Dictionary<int, string>(_slotOwnership),
            CharacterPool = characterPoolDtos,
            CardCollection = _cardCollection.ToList(),
            SharedGold = SharedGold,
            PlayerStates = playerStateDtos,
            BattleHistory = _battleHistory.ToList(),
        };
    }

    public void RestoreFrom(RunDto dto, IReadOnlyDictionary<string, CharacterInstance>? instanceLookup = null)
    {
        ArgumentNullException.ThrowIfNull(dto);

        RunId = dto.RunId;
        StoryId = dto.StoryId;
        CurrentRing = dto.CurrentRing;
        MaxRing = dto.MaxRing;
        Phase = dto.Phase;
        RunSeed = dto.RunSeed;
        IsMultiplayer = dto.IsMultiplayer;
        SharedGold = dto.SharedGold;

        _cardCollection.Clear();
        foreach (var cardId in dto.CardCollection)
            _cardCollection.Add(cardId);

        _characterPool.Clear();
        instanceLookup ??= new Dictionary<string, CharacterInstance>(StringComparer.Ordinal);
        foreach (var entry in dto.CharacterPool)
        {
            if (instanceLookup.TryGetValue(entry.InstanceId, out var existing))
            {
                _characterPool.Add(existing);
                continue;
            }

            var instance = new CharacterInstance(
                new CharacterDto { Id = entry.DefinitionId, Cards = entry.DefinitionCardIds },
                entry.InstanceId);
            if (entry.Decks.Count > 0)
            {
                instance.ApplyDeckSnapshots(
                    entry.Decks.Select(d => (IReadOnlyList<string>)d.CardIds).ToList(),
                    entry.CurrentDeckIndex);
            }
            _characterPool.Add(instance);
        }

        _slotOwnership.Clear();
        foreach (var (slot, playerId) in dto.SlotOwnership)
            _slotOwnership[slot] = playerId;

        _playerControllers.Clear();
        foreach (var pcDto in dto.PlayerControllers)
            _playerControllers.Add(new PlayerController(pcDto.PlayerId, pcDto.DisplayName, pcDto.IsOwner));

        _battleHistory.Clear();
        _battleHistory.AddRange(dto.BattleHistory);

        for (var i = 0; i < Math.Min(dto.PlayerStates.Count, RunConstants.SlotCount); i++)
        {
            var stateDto = dto.PlayerStates[i];
            PlayerStates[i].SetGold(stateDto.Gold);
            PlayerStates[i].Modifiers.Clear();
            PlayerStates[i].Modifiers.AddRange(stateDto.Modifiers);
            PlayerStates[i].EventFlags.Clear();
            foreach (var (k, v) in stateDto.EventFlags)
                PlayerStates[i].EventFlags[k] = v;

            if (stateDto.ActiveCharacterIndex.HasValue
                && stateDto.ActiveCharacterIndex.Value >= 0
                && stateDto.ActiveCharacterIndex.Value < _characterPool.Count)
            {
                PlayerStates[i].SetActiveCharacter(_characterPool[stateDto.ActiveCharacterIndex.Value]);
            }
            else
            {
                PlayerStates[i].SetActiveCharacter(null);
            }
        }
    }
}