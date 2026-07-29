# Run Mod 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现局内数据模块 Run Mod，管理 Run 级生命周期、角色池、卡牌收集、金币、修饰器、控制权和持久化。

**Architecture:** 采用 MVC 模式，`RunMod` 继承 `BaseMod` 作为数据持有，`RunController` 继承 `BaseController<RunMod>` 作为编排者。全队共享 CharacterPool/CardCollection，槽位独立 Gold/Modifiers/EventFlags。多人通过 SlotOwnership 管理控制权。

**Tech Stack:** C# / .NET 8 / NUnit 4 / Godot Mono 4.6.1

---

## 文件结构

| 文件 | 职责 |
|---|---|
| `Src/mod/run/ERunPhase.cs` | Run 阶段枚举 |
| `Src/mod/run/RunConstants.cs` | Run 常量 |
| `Src/mod/run/RunDto.cs` | Run 快照 DTO（含子类型） |
| `Src/mod/run/PlayerRunState.cs` | 槽位游戏数据类 |
| `Src/mod/run/RunMod.cs` | Run 数据持有（BaseMod） |
| `Src/mod/run/PlayerController.cs` | 人类玩家控制器信息 |
| `Src/mod/run/save/RunSaveService.cs` | 存档/读档服务 |
| `Src/mod/run/events/RunEventBus.cs` | Run 级事件定义 |
| `Src/mod/run/reward/RunRewardDistributor.cs` | 奖励分发逻辑 |
| `Src/mod/run/RunController.cs` | Run 生命周期编排（BaseController） |
| `Tests/kemo_card.Ui.Tests/Run/RunModTests.cs` | RunMod 单元测试 |
| `Tests/kemo_card.Ui.Tests/Run/RunSaveServiceTests.cs` | 存档服务测试 |
| `Tests/kemo_card.Ui.Tests/Run/RunControllerTests.cs` | RunController 测试 |
| `Tests/kemo_card.Ui.Tests/Run/RunIntegrationTests.cs` | 集成测试 |

---

### Task 1: ERunPhase 枚举

**Files:**
- Create: `Src/mod/run/ERunPhase.cs`

- [ ] **Step 1: 编写枚举定义**

```csharp
namespace KemoCard.Mod.Run;

public enum ERunPhase
{
    Init,
    Event,
    Reward,
    Battle,
    BattleEnd,
    RingEnd,
    Finished,
}
```

- [ ] **Step 2: Compile check**

Run: `dotnet build Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj`

- [ ] **Step 3: Commit**

```bash
git add Src/mod/run/ERunPhase.cs
git commit -m "feat: 添加 ERunPhase Run 阶段枚举"
```

---

### Task 2: RunConstants 常量

**Files:**
- Create: `Src/mod/run/RunConstants.cs`

- [ ] **Step 1: 编写常量类**

```csharp
namespace KemoCard.Mod.Run;

public static class RunConstants
{
    public const int MaxPlayers = 4;
    public const int SlotCount = 4;
    public const int MaxSlotsPerPlayer = 4;
    public const int MinSlotsPerPlayer = 1;
    public const int DefaultMaxRings = 3;
}
```

- [ ] **Step 2: Compile check**

Run: `dotnet build Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj`

- [ ] **Step 3: Commit**

```bash
git add Src/mod/run/RunConstants.cs
git commit -m "feat: 添加 RunConstants 常量定义"
```

---

### Task 3: DTO 类型（RunDto, PlayerRunStateDto 等）

**Files:**
- Create: `Src/mod/run/RunDto.cs`

- [ ] **Step 1: 编写所有 DTO 记录类型**

```csharp
using KemoCard.Mod.Combat;

namespace KemoCard.Mod.Run;

public sealed record PlayerControllerDto
{
    public string PlayerId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsOwner { get; init; }
}

public sealed record PlayerRunStateDto
{
    public int? ActiveCharacterIndex { get; init; }
    public int Gold { get; init; }
    public List<RunModifierDto> Modifiers { get; init; } = [];
    public Dictionary<string, object> EventFlags { get; init; } = new(StringComparer.Ordinal);
}

public sealed record CharacterPoolEntryDto
{
    public string DefinitionId { get; init; } = "";
    public string InstanceId { get; init; } = "";
    public List<DeckSnapshotDto> Decks { get; init; } = [];
    public int CurrentDeckIndex { get; init; }
}

public sealed record DeckSnapshotDto
{
    public List<string> CardIds { get; init; } = [];
}

public sealed record RunModifierDto
{
    public string ModifierId { get; init; } = "";
    public float Value { get; init; }
    public string Source { get; init; } = "";
}

public sealed record BattleRecordDto
{
    public string BattleId { get; init; } = "";
    public bool Won { get; init; }
    public int TurnsUsed { get; init; }
    public int DamageDealt { get; init; }
    public int DamageTaken { get; init; }
}

public sealed record RunDto
{
    public string RunId { get; init; } = "";
    public int SchemaVersion { get; init; } = 1;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    public int CurrentRing { get; init; } = 1;
    public int MaxRing { get; init; } = RunConstants.DefaultMaxRings;
    public ERunPhase Phase { get; init; } = ERunPhase.Init;
    public int RunSeed { get; init; }

    public bool IsMultiplayer { get; init; }
    public List<PlayerControllerDto> PlayerControllers { get; init; } = [];
    public Dictionary<int, string> SlotOwnership { get; init; } = new();

    public List<CharacterPoolEntryDto> CharacterPool { get; init; } = [];
    public List<string> CardCollection { get; init; } = [];
    public int SharedGold { get; init; }

    public List<PlayerRunStateDto> PlayerStates { get; init; } = [];
    public List<BattleRecordDto> BattleHistory { get; init; } = [];
}
```

- [ ] **Step 2: Compile check**

Run: `dotnet build Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj`

- [ ] **Step 3: Commit**

```bash
git add Src/mod/run/RunDto.cs
git commit -m "feat: 添加 RunDto 及相关 DTO 类型定义"
```

---

### Task 4: PlayerRunState 运行时类

**Files:**
- Create: `Src/mod/run/PlayerRunState.cs`

- [ ] **Step 1: 编写 PlayerRunState 类**

```csharp
using KemoCard.Mod.Combat;

namespace KemoCard.Mod.Run;

public sealed class PlayerRunState
{
    public CharacterInstance? ActiveCharacter { get; private set; }
    public int Gold { get; private set; }
    public List<RunModifierDto> Modifiers { get; } = [];
    public Dictionary<string, object> EventFlags { get; } = new(StringComparer.Ordinal);

    public void SetActiveCharacter(CharacterInstance? character)
    {
        ActiveCharacter = character;
    }

    public void SetGold(int amount)
    {
        Gold = Math.Max(0, amount);
    }

    public void AddGold(int amount)
    {
        if (amount <= 0)
            return;
        Gold += amount;
    }

    public bool SpendGold(int amount)
    {
        if (amount <= 0)
            return true;
        if (Gold < amount)
            return false;
        Gold -= amount;
        return true;
    }

    public void AddModifier(RunModifierDto modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        Modifiers.Add(modifier);
    }

    public void SetEventFlag(string key, object value)
    {
        EventFlags[key] = value;
    }

    public T? GetEventFlag<T>(string key)
    {
        if (EventFlags.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return default;
    }

    public PlayerRunStateDto ToDto()
    {
        return new PlayerRunStateDto
        {
            ActiveCharacterIndex = null,
            Gold = Gold,
            Modifiers = [.. Modifiers],
            EventFlags = new Dictionary<string, object>(EventFlags, StringComparer.Ordinal),
        };
    }
}
```

- [ ] **Step 2: Compile check**

Run: `dotnet build Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj`

- [ ] **Step 3: Commit**

```bash
git add Src/mod/run/PlayerRunState.cs
git commit -m "feat: 添加 PlayerRunState 槽位游戏数据类"
```

---

### Task 5: PlayerController 类

**Files:**
- Create: `Src/mod/run/PlayerController.cs`

- [ ] **Step 1: 编写 PlayerController**

```csharp
namespace KemoCard.Mod.Run;

public sealed class PlayerController
{
    public string PlayerId { get; }
    public string DisplayName { get; private set; }
    public bool IsOwner { get; }

    public PlayerController(string playerId, string displayName, bool isOwner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        PlayerId = playerId;
        DisplayName = displayName;
        IsOwner = isOwner;
    }

    public void SetDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
    }

    public PlayerControllerDto ToDto()
    {
        return new PlayerControllerDto
        {
            PlayerId = PlayerId,
            DisplayName = DisplayName,
            IsOwner = IsOwner,
        };
    }
}
```

- [ ] **Step 2: Compile check**

Run: `dotnet build Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj`

- [ ] **Step 3: Commit**

```bash
git add Src/mod/run/PlayerController.cs
git commit -m "feat: 添加 PlayerController 玩家控制权类"
```

---

### Task 6: RunMod 数据持有

**Files:**
- Create: `Src/mod/run/RunMod.cs`
- Create: `Tests/kemo_card.Ui.Tests/Run/RunModTests.cs`

- [ ] **Step 1: 编写 RunModTests 测试**

```csharp
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Run;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunModTests
{
    [Test]
    public void ToDto_round_trips_shared_data()
    {
        var mod = new RunMod { RunId = "r1", RunSeed = 42, IsMultiplayer = true, CurrentRing = 2 };
        var dto = mod.ToDto();

        Assert.That(dto.RunId, Is.EqualTo("r1"));
        Assert.That(dto.RunSeed, Is.EqualTo(42));
        Assert.That(dto.IsMultiplayer, Is.True);
        Assert.That(dto.CurrentRing, Is.EqualTo(2));
        Assert.That(dto.Phase, Is.EqualTo(ERunPhase.Event));
    }

    [Test]
    public void ToDto_and_RestoreFrom_preserves_character_pool()
    {
        var mod = new RunMod();
        mod.AddToCharacterPool(new Combat.CharacterInstance(new CharacterDto { Id = "test", Cards = [] }));
        var dto = mod.ToDto();

        var restored = new RunMod();
        restored.RestoreFrom(dto, new Dictionary<string, Combat.CharacterInstance>());

        Assert.That(restored.CharacterPool, Has.Count.EqualTo(1));
    }

    [Test]
    public void ToDto_and_RestoreFrom_preserves_card_collection()
    {
        var mod = new RunMod();
        mod.CardCollection.Add("card.a");
        mod.CardCollection.Add("card.b");

        var dto = mod.ToDto();
        var restored = new RunMod();
        restored.RestoreFrom(dto, new Dictionary<string, Combat.CharacterInstance>());

        Assert.That(restored.CardCollection, Is.EquivalentTo(new[] { "card.a", "card.b" }));
    }

    [Test]
    public void ToDto_and_RestoreFrom_preserves_slot_ownership()
    {
        var mod = new RunMod { IsMultiplayer = true };
        mod.SlotOwnership[0] = "p1";
        mod.SlotOwnership[1] = "p2";
        mod.SlotOwnership[2] = "p1";
        mod.SlotOwnership[3] = "p2";

        var dto = mod.ToDto();
        var restored = new RunMod();
        restored.RestoreFrom(dto, new Dictionary<string, Combat.CharacterInstance>());

        Assert.That(restored.SlotOwnership[0], Is.EqualTo("p1"));
        Assert.That(restored.SlotOwnership[1], Is.EqualTo("p2"));
        Assert.That(restored.SlotOwnership[2], Is.EqualTo("p1"));
        Assert.That(restored.SlotOwnership[3], Is.EqualTo("p2"));
    }

    [Test]
    public void ToDto_and_RestoreFrom_preserves_player_states_gold_and_modifiers()
    {
        var mod = new RunMod { IsMultiplayer = true };
        mod.PlayerStates[0].SetGold(100);
        mod.PlayerStates[0].AddModifier(new RunModifierDto { ModifierId = "atk_up", Value = 5f, Source = "event.1" });
        mod.PlayerStates[1].SetGold(50);

        var dto = mod.ToDto();
        var restored = new RunMod();
        restored.RestoreFrom(dto, new Dictionary<string, Combat.CharacterInstance>());

        Assert.That(restored.PlayerStates[0].Gold, Is.EqualTo(100));
        Assert.That(restored.PlayerStates[0].Modifiers, Has.Count.EqualTo(1));
        Assert.That(restored.PlayerStates[0].Modifiers[0].ModifierId, Is.EqualTo("atk_up"));
        Assert.That(restored.PlayerStates[1].Gold, Is.EqualTo(50));
    }

    [Test]
    public void ToDto_and_RestoreFrom_preserves_shared_gold_in_singleplayer()
    {
        var mod = new RunMod { IsMultiplayer = false };
        mod.SharedGold = 999;

        var dto = mod.ToDto();
        var restored = new RunMod();
        restored.RestoreFrom(dto, new Dictionary<string, Combat.CharacterInstance>());

        Assert.That(restored.SharedGold, Is.EqualTo(999));
    }

    [Test]
    public void ActiveParty_is_computed_from_player_states()
    {
        var mod = new RunMod { IsMultiplayer = false };
        var characters = new List<Combat.CharacterInstance>
        {
            new(new CharacterDto { Id = "c0", Cards = [] }),
            new(new CharacterDto { Id = "c1", Cards = [] }),
            new(new CharacterDto { Id = "c2", Cards = [] }),
            new(new CharacterDto { Id = "c3", Cards = [] }),
        };
        foreach (var c in characters)
            mod.AddToCharacterPool(c);
        for (var i = 0; i < 4; i++)
            mod.PlayerStates[i].SetActiveCharacter(characters[i]);

        var party = mod.ActiveParty;

        Assert.That(party, Has.Length.EqualTo(4));
        Assert.That(party[0], Is.SameAs(characters[0]));
        Assert.That(party[3], Is.SameAs(characters[3]));
    }

    [Test]
    public void ValidateParty_fails_when_any_slot_is_null()
    {
        var mod = new RunMod();
        Assert.That(mod.ValidateParty(), Is.False);

        mod.PlayerStates[0].SetActiveCharacter(new Combat.CharacterInstance());
        mod.PlayerStates[1].SetActiveCharacter(new Combat.CharacterInstance());
        mod.PlayerStates[2].SetActiveCharacter(new Combat.CharacterInstance());
        Assert.That(mod.ValidateParty(), Is.False);

        mod.PlayerStates[3].SetActiveCharacter(new Combat.CharacterInstance());
        Assert.That(mod.ValidateParty(), Is.True);
    }

    [Test]
    public void Can_user_edit_character_uses_CardCollection_for_buildable_cards()
    {
        var mod = new RunMod();
        mod.CardCollection.Add("card.x");
        mod.CardCollection.Add("card.y");

        Assert.That(mod.CanUseCard("card.x"), Is.True);
        Assert.That(mod.CanUseCard("card.y"), Is.True);
        Assert.That(mod.CanUseCard("card.z"), Is.False);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunModTests"`

Expected: FAIL — build error (RunMod 尚未实现)

- [ ] **Step 3: 编写 RunMod 实现**

```csharp
using KemoCard.Frame.Content;
using KemoCard.Frame.Mvc;
using KemoCard.Mod.Combat;

namespace KemoCard.Mod.Run;

public sealed class RunMod : BaseMod
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

    public string RunId { get; set; }
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

    public RunDto ToDto()
    {
        var characterPoolDtos = _characterPool.Select(c => new CharacterPoolEntryDto
        {
            DefinitionId = c.DefinitionId,
            InstanceId = c.InstanceId,
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
        RunId = dto.RunId;
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
        if (instanceLookup != null)
        {
            foreach (var entry in dto.CharacterPool)
            {
                if (instanceLookup.TryGetValue(entry.InstanceId, out var instance))
                    _characterPool.Add(instance);
            }
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunModTests"`

Expected: All 8 tests PASS

- [ ] **Step 5: Commit**

```bash
git add Src/mod/run/RunMod.cs Tests/kemo_card.Ui.Tests/Run/RunModTests.cs
git commit -m "feat: 实现 RunMod 数据持有及 ToDto/RestoreFrom 序列化"
```

---

### Task 7: RunSaveService 存档服务

**Files:**
- Create: `Src/mod/run/save/RunSaveService.cs`
- Create: `Tests/kemo_card.Ui.Tests/Run/RunSaveServiceTests.cs`

- [ ] **Step 1: 编写 RunSaveServiceTests 测试**

```csharp
using System.Text.Json;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Save;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunSaveServiceTests
{
    private string _tempDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"run_save_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void Save_and_LoadOrDefault_round_trips()
    {
        var service = new RunSaveService(_tempDir);
        var dto = new RunDto
        {
            RunId = "r-test-001",
            CurrentRing = 2,
            RunSeed = 42,
            SharedGold = 100,
        };

        service.Save(dto);
        var loaded = service.LoadOrDefault();

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.RunId, Is.EqualTo("r-test-001"));
        Assert.That(loaded.CurrentRing, Is.EqualTo(2));
        Assert.That(loaded.RunSeed, Is.EqualTo(42));
        Assert.That(loaded.SharedGold, Is.EqualTo(100));
    }

    [Test]
    public void Save_creates_exists()
    {
        var service = new RunSaveService(_tempDir);
        Assert.That(service.Exists, Is.False);

        service.Save(new RunDto { RunId = "r-exists" });
        Assert.That(service.Exists, Is.True);
    }

    [Test]
    public void Delete_removes_file()
    {
        var service = new RunSaveService(_tempDir);
        service.Save(new RunDto { RunId = "r-del" });
        Assert.That(service.Exists, Is.True);

        service.Delete();
        Assert.That(service.Exists, Is.False);
    }

    [Test]
    public void LoadOrDefault_returns_default_when_no_file()
    {
        var service = new RunSaveService(_tempDir);
        var loaded = service.LoadOrDefault();

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.RunId, Is.Empty);
        Assert.That(loaded.Phase, Is.EqualTo(ERunPhase.Init));
    }

    [Test]
    public void LoadOrDefault_falls_back_to_backup_when_primary_corrupt()
    {
        var service = new RunSaveService(_tempDir);
        var validDto = new RunDto { RunId = "r-backup", RunSeed = 99 };
        service.Save(validDto);

        var primaryPath = Path.Combine(_tempDir, "r-backup.json");
        File.WriteAllText(primaryPath, "not valid json{{{");

        var loaded = service.LoadOrDefault();
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.RunSeed, Is.EqualTo(99));
    }

    [Test]
    public void LoadOrDefault_returns_default_when_both_files_corrupt()
    {
        var service = new RunSaveService(_tempDir);
        var validDto = new RunDto { RunId = "r-both-corrupt", RunSeed = 77 };
        service.Save(validDto);

        var primaryPath = Path.Combine(_tempDir, "r-both-corrupt.json");
        var backupPath = Path.Combine(_tempDir, "r-both-corrupt.bak.json");
        File.WriteAllText(primaryPath, "bad");
        File.WriteAllText(backupPath, "also bad");

        var loaded = service.LoadOrDefault();
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded.RunId, Is.Empty);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunSaveServiceTests"`

Expected: FAIL — build error (RunSaveService 尚未实现)

- [ ] **Step 3: 实现 RunSaveService**

```csharp
using System.Text.Json;

namespace KemoCard.Mod.Run.Save;

public sealed class RunSaveService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _directoryPath;
    private readonly object _ioGate = new();
    private readonly Action<string>? _logWarning;

    public RunSaveService(string directoryPath, Action<string>? logWarning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = directoryPath;
        _logWarning = logWarning;
        Directory.CreateDirectory(directoryPath);
    }

    public bool Exists
    {
        get
        {
            lock (_ioGate)
            {
                return Directory.GetFiles(_directoryPath, "*.json")
                    .Any(f => !f.EndsWith(".bak.json", StringComparison.OrdinalIgnoreCase)
                           && !f.EndsWith(".tmp.json", StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    public RunDto LoadOrDefault()
    {
        lock (_ioGate)
        {
            var primaryFiles = Directory.GetFiles(_directoryPath, "*.json")
                .Where(f => !f.EndsWith(".bak.json", StringComparison.OrdinalIgnoreCase)
                         && !f.EndsWith(".tmp.json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var file in primaryFiles)
            {
                if (TryRead(file, out var dto, isPrimary: true))
                    return dto;
            }

            var backupFiles = Directory.GetFiles(_directoryPath, "*.bak.json");
            foreach (var file in backupFiles)
            {
                if (TryRead(file, out var dto, isPrimary: false))
                    return dto;
            }

            return new RunDto();
        }
    }

    public void Save(RunDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrEmpty(dto.RunId))
            return;

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        lock (_ioGate)
        {
            var filePath = Path.Combine(_directoryPath, $"{dto.RunId}.json");
            var backupPath = Path.Combine(_directoryPath, $"{dto.RunId}.bak.json");
            var tempPath = Path.Combine(_directoryPath, $"{dto.RunId}.tmp.json");

            File.WriteAllText(tempPath, json);
            if (File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, filePath);
            }
        }
    }

    public void Delete()
    {
        lock (_ioGate)
        {
            foreach (var file in Directory.GetFiles(_directoryPath, "*.json"))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    private bool TryRead(string path, out RunDto dto, bool isPrimary)
    {
        dto = new RunDto();
        if (!File.Exists(path))
            return false;

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<RunDto>(json, JsonOptions);
            if (loaded == null)
                return false;
            dto = loaded;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            LogWarning($"Run save at '{path}' failed to load: {ex.Message}");
            if (isPrimary)
            {
                try { File.Delete(path); } catch { }
            }
            return false;
        }
    }

    private void LogWarning(string message)
    {
        if (_logWarning != null)
            _logWarning(message);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunSaveServiceTests"`

Expected: All 6 tests PASS

- [ ] **Step 5: Commit**

```bash
git add Src/mod/run/save/RunSaveService.cs Tests/kemo_card.Ui.Tests/Run/RunSaveServiceTests.cs
git commit -m "feat: 实现 RunSaveService 存档服务及测试"
```

---

### Task 8: RunEventBus 事件定义

**Files:**
- Create: `Src/mod/run/events/RunEventBus.cs`

- [ ] **Step 1: 编写事件定义**

```csharp
using KemoCard.Frame.Mvc;

namespace KemoCard.Mod.Run.Events;

public enum ERunEvent
{
    [EventPayload(typeof(RunPhaseChangedPayload))]
    RunPhaseChanged,

    [EventPayload(typeof(RunSlotOwnershipChangedPayload))]
    RunSlotOwnershipChanged,

    [EventPayload(typeof(RunGoldChangedPayload))]
    RunGoldChanged,
}

public readonly struct RunPhaseChangedPayload
{
    public ERunPhase PreviousPhase { get; init; }
    public ERunPhase CurrentPhase { get; init; }
}

public readonly struct RunSlotOwnershipChangedPayload
{
    public int SlotIndex { get; init; }
    public string? PreviousPlayerId { get; init; }
    public string? CurrentPlayerId { get; init; }
}

public readonly struct RunGoldChangedPayload
{
    public int? SlotIndex { get; init; }
    public int PreviousAmount { get; init; }
    public int CurrentAmount { get; init; }
}

[EventTable(typeof(ERunEvent), typeof(RunMod))]
public static partial class RunModEventTable
{
}
```

- [ ] **Step 2: Compile check**

Run: `dotnet build Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj`

- [ ] **Step 3: Commit**

```bash
git add Src/mod/run/events/RunEventBus.cs
git commit -m "feat: 添加 Run 级事件定义 RunEventBus"
```

---

### Task 9: RunRewardDistributor 奖励分发器

**Files:**
- Create: `Src/mod/run/reward/RunRewardDistributor.cs`
- Create: `Tests/kemo_card.Ui.Tests/Run/RunRewardDistributorTests.cs`

- [ ] **Step 1: 编写测试**

```csharp
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Reward;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunRewardDistributorTests
{
    [Test]
    public void GenerateRewards_creates_equal_option_count_per_slot()
    {
        var distributor = new RunRewardDistributor();
        var states = new PlayerRunState?[] { new(), new(), null, null };
        var registry = new GameDefinitionRegistry();
        var rng = new KemoCard.Frame.Scripting.HostRng(42, "test");

        var result = distributor.GenerateRewards(states, registry, ringIndex: 1, optionCount: 3, rng);

        Assert.That(result.Rewards.Count, Is.EqualTo(2));
        foreach (var (_, options) in result.Rewards)
        {
            Assert.That(options.Options, Has.Length.EqualTo(3));
        }
    }

    [Test]
    public void GenerateRewards_skips_null_slots()
    {
        var distributor = new RunRewardDistributor();
        var states = new PlayerRunState?[] { new(), null, new(), null };
        var registry = new GameDefinitionRegistry();
        var rng = new KemoCard.Frame.Scripting.HostRng(99, "test");

        var result = distributor.GenerateRewards(states, registry, ringIndex: 2, optionCount: 2, rng);

        Assert.That(result.Rewards.Count, Is.EqualTo(2));
        Assert.That(result.Rewards.ContainsKey(0));
        Assert.That(result.Rewards.ContainsKey(2));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunRewardDistributorTests"`

Expected: FAIL — build error

- [ ] **Step 3: 实现 RunRewardDistributor**

```csharp
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;

namespace KemoCard.Mod.Run.Reward;

public enum ERewardType
{
    Character,
    Card,
    Gold,
    Modifier,
    Heal,
}

public readonly struct RewardOptionDto
{
    public ERewardType Type { get; init; }
    public string DisplayName { get; init; }
    public object? Data { get; init; }
}

public readonly struct RewardOptionsDto
{
    public RewardOptionDto[] Options { get; init; }
}

public readonly struct PerPlayerRewardSet
{
    public Dictionary<int, RewardOptionsDto> Rewards { get; init; }
}

public sealed class RunRewardDistributor
{
    public PerPlayerRewardSet GenerateRewards(
        IReadOnlyList<PlayerRunState?> playerStates,
        GameDefinitionRegistry definitions,
        int ringIndex,
        int optionCount,
        HostRng rng)
    {
        ArgumentNullException.ThrowIfNull(playerStates);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(rng);

        var rewards = new Dictionary<int, RewardOptionsDto>();
        for (var slot = 0; slot < playerStates.Count; slot++)
        {
            if (playerStates[slot] == null)
                continue;

            var slotRng = new HostRng(rng.NextInt(0, int.MaxValue), $"reward.p{slot}");
            var options = new RewardOptionDto[optionCount];
            for (var i = 0; i < optionCount; i++)
            {
                options[i] = new RewardOptionDto
                {
                    Type = ERewardType.Gold,
                    DisplayName = $"Gold x{slotRng.NextInt(10, 51)}",
                };
            }

            rewards[slot] = new RewardOptionsDto { Options = options };
        }

        return new PerPlayerRewardSet { Rewards = rewards };
    }

    public void ApplyReward(PlayerRunState player, RewardOptionsDto options, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (selectedIndex < 0 || selectedIndex >= options.Options.Length)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));

        _ = options.Options[selectedIndex];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunRewardDistributorTests"`

Expected: 2 tests PASS

- [ ] **Step 5: Commit**

```bash
git add Src/mod/run/reward/RunRewardDistributor.cs Tests/kemo_card.Ui.Tests/Run/RunRewardDistributorTests.cs
git commit -m "feat: 实现 RunRewardDistributor 奖励分发器"
```

---

### Task 10: RunController 创建与阶段管理

**Files:**
- Create: `Src/mod/run/RunController.cs`
- Create: `Tests/kemo_card.Ui.Tests/Run/RunControllerTests.cs`

- [ ] **Step 1: 编写 RunController 核心测试**

```csharp
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Reward;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunControllerTests
{
    [Test]
    public void CreateRun_sets_initial_state()
    {
        var controller = new RunController(new RunMod());
        var candidates = new List<CharacterDto>
        {
            new() { Id = "hero_a", Cards = [] },
            new() { Id = "hero_b", Cards = [] },
            new() { Id = "hero_c", Cards = [] },
        };
        var rng = new HostRng(1, "create");

        var dto = controller.CreateRun(rng, candidates, isMultiplayer: false);

        Assert.That(dto.Phase, Is.EqualTo(ERunPhase.Event));
        Assert.That(dto.CurrentRing, Is.EqualTo(1));
        Assert.That(dto.RunId, Is.Not.Empty);
        Assert.That(dto.IsMultiplayer, Is.False);
    }

    [Test]
    public void CreateRun_singleplayer_has_local_controller_with_all_slots()
    {
        var controller = new RunController(new RunMod());
        var rng = new HostRng(2, "create");

        var dto = controller.CreateRun(rng, [], isMultiplayer: false);

        Assert.That(dto.PlayerControllers, Has.Count.EqualTo(1));
        Assert.That(dto.PlayerControllers[0].PlayerId, Is.EqualTo("local"));
        Assert.That(dto.PlayerControllers[0].IsOwner, Is.True);
        Assert.That(dto.SlotOwnership.Count, Is.EqualTo(4));
        for (var i = 0; i < 4; i++)
            Assert.That(dto.SlotOwnership[i], Is.EqualTo("local"));
    }

    [Test]
    public void AddToCharacterPool_adds_character()
    {
        var controller = new RunController(new RunMod());
        var character = new CharacterInstance(
            new CharacterDto { Id = "test", Cards = [] }, "inst-1");

        var result = controller.AddToCharacterPool(character);

        Assert.That(result, Is.True);
        Assert.That(controller.Model.CharacterPool, Has.Count.EqualTo(1));
        Assert.That(controller.Model.CharacterPool[0].InstanceId, Is.EqualTo("inst-1"));
    }

    [Test]
    public void SetActiveCharacter_updates_slot()
    {
        var controller = new RunController(new RunMod());
        var character = new CharacterInstance(
            new CharacterDto { Id = "test", Cards = [] }, "inst-1");
        controller.AddToCharacterPool(character);

        var result = controller.SetActiveCharacter(slotIndex: 0, poolIndex: 0);

        Assert.That(result, Is.True);
        Assert.That(controller.Model.PlayerStates[0].ActiveCharacter, Is.SameAs(character));
    }

    [Test]
    public void SetActiveCharacter_fails_for_out_of_range_pool_index()
    {
        var controller = new RunController(new RunMod());

        var result = controller.SetActiveCharacter(slotIndex: 0, poolIndex: 99);

        Assert.That(result, Is.False);
    }

    [Test]
    public void UnsetActiveCharacter_clears_slot()
    {
        var controller = new RunController(new RunMod());
        var character = new CharacterInstance(
            new CharacterDto { Id = "test", Cards = [] }, "inst-1");
        controller.AddToCharacterPool(character);
        controller.SetActiveCharacter(0, 0);

        var result = controller.UnsetActiveCharacter(0);

        Assert.That(result, Is.True);
        Assert.That(controller.Model.PlayerStates[0].ActiveCharacter, Is.Null);
    }

    [Test]
    public void ValidateParty_all_slots_must_be_non_null()
    {
        var controller = new RunController(new RunMod());
        for (var i = 0; i < 4; i++)
        {
            var character = new CharacterInstance(
                new CharacterDto { Id = $"test_{i}", Cards = [] }, $"inst-{i}");
            controller.AddToCharacterPool(character);
        }
        controller.SetActiveCharacter(0, 0);
        controller.SetActiveCharacter(1, 1);
        controller.SetActiveCharacter(2, 2);

        Assert.That(controller.ValidateParty(), Is.False);

        controller.SetActiveCharacter(3, 3);
        Assert.That(controller.ValidateParty(), Is.True);
    }

    [Test]
    public void Gold_operations_singleplayer_use_shared_gold()
    {
        var mod = new RunMod { IsMultiplayer = false };
        var controller = new RunController(mod);

        controller.AddGold(slotIndex: 0, amount: 100);
        var gold = controller.GetGold(slotIndex: null);

        Assert.That(gold, Is.EqualTo(100));
        Assert.That(controller.Model.SharedGold, Is.EqualTo(100));
    }

    [Test]
    public void SpendGold_singleplayer_deducts_shared_gold()
    {
        var mod = new RunMod { IsMultiplayer = false, SharedGold = 200 };
        var controller = new RunController(mod);

        var result = controller.SpendGold(slotIndex: 0, amount: 50);

        Assert.That(result, Is.True);
        Assert.That(controller.Model.SharedGold, Is.EqualTo(150));
    }

    [Test]
    public void SpendGold_fails_when_insufficient()
    {
        var mod = new RunMod { IsMultiplayer = false, SharedGold = 30 };
        var controller = new RunController(mod);

        var result = controller.SpendGold(slotIndex: 0, amount: 100);

        Assert.That(result, Is.False);
        Assert.That(controller.Model.SharedGold, Is.EqualTo(30));
    }

    [Test]
    public void Gold_operations_multiplayer_use_slot_gold()
    {
        var mod = new RunMod { IsMultiplayer = true };
        var controller = new RunController(mod);

        controller.AddGold(slotIndex: 1, amount: 80);
        var gold = controller.GetGold(slotIndex: 1);

        Assert.That(gold, Is.EqualTo(80));
        Assert.That(controller.Model.PlayerStates[1].Gold, Is.EqualTo(80));
    }

    [Test]
    public void TransferGold_moves_between_slots_in_multiplayer()
    {
        var mod = new RunMod { IsMultiplayer = true };
        var controller = new RunController(mod);
        controller.AddGold(slotIndex: 0, amount: 100);

        var result = controller.TransferGold(fromSlot: 0, toSlot: 2, amount: 40);

        Assert.That(result, Is.True);
        Assert.That(controller.Model.PlayerStates[0].Gold, Is.EqualTo(60));
        Assert.That(controller.Model.PlayerStates[2].Gold, Is.EqualTo(40));
    }

    [Test]
    public void TransferGold_fails_in_non_multiplayer()
    {
        var mod = new RunMod { IsMultiplayer = false, SharedGold = 100 };
        var controller = new RunController(mod);

        var result = controller.TransferGold(fromSlot: 0, toSlot: 2, amount: 40);

        Assert.That(result, Is.False);
    }

    [Test]
    public void NextRing_advances_and_returns_has_next()
    {
        var mod = new RunMod { MaxRing = 3, CurrentRing = 1 };
        var controller = new RunController(mod);

        Assert.That(controller.HasNextRing(), Is.True);
        controller.NextRing();
        Assert.That(controller.Model.CurrentRing, Is.EqualTo(2));
        Assert.That(controller.HasNextRing(), Is.True);

        controller.NextRing();
        Assert.That(controller.Model.CurrentRing, Is.EqualTo(3));
        Assert.That(controller.HasNextRing(), Is.False);
    }

    [Test]
    public void AbandonRun_sets_phase_to_finished()
    {
        var mod = new RunMod { Phase = ERunPhase.Battle };
        var controller = new RunController(mod);

        var dto = controller.AbandonRun();

        Assert.That(dto.Phase, Is.EqualTo(ERunPhase.Finished));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunControllerTests"`

Expected: FAIL — build error

- [ ] **Step 3: 编写 RunController 实现**

```csharp
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run.Events;
using KemoCard.Mod.Run.Reward;

namespace KemoCard.Mod.Run;

public sealed class RunController : BaseController<RunMod>
{
    private readonly RunRewardDistributor _rewardDistributor = new();
    private RunDto? _battleSnapshot;

    public RunController(RunMod model) : base(model) { }

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

    #region 持久化

    public void Save(Save.RunSaveService saveService)
    {
        ArgumentNullException.ThrowIfNull(saveService);
        saveService.Save(Model.ToDto());
    }

    public bool TryLoad(Save.RunSaveService saveService, out RunDto dto)
    {
        ArgumentNullException.ThrowIfNull(saveService);
        var loaded = saveService.LoadOrDefault();
        if (loaded == null || string.IsNullOrEmpty(loaded.RunId))
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunControllerTests"`

Expected: All 16 tests PASS

- [ ] **Step 5: Commit**

```bash
git add Src/mod/run/RunController.cs Tests/kemo_card.Ui.Tests/Run/RunControllerTests.cs
git commit -m "feat: 实现 RunController 核心逻辑"
```

---

### Task 11: RunController 控制权管理

**Files:**
- Modify: `Src/mod/run/RunController.cs`
- Modify: `Tests/kemo_card.Ui.Tests/Run/RunControllerTests.cs`

- [ ] **Step 1: 追加控制权测试到 RunControllerTests**

在 `RunControllerTests` 类末尾追加：

```csharp
[Test]
public void AssignSlot_sets_slot_ownership()
{
    var mod = new RunMod { IsMultiplayer = true, Phase = ERunPhase.Event };
    mod.PlayerStates[0].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[1].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[2].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[3].SetActiveCharacter(new CharacterInstance());
    mod.AddPlayerController(new PlayerController("owner", "Owner", true));
    mod.AddPlayerController(new PlayerController("p2", "Player2", false));
    var controller = new RunController(mod);

    var result = controller.AssignSlot(slotIndex: 0, playerId: "p2");

    Assert.That(result, Is.True);
    Assert.That(mod.SlotOwnership[0], Is.EqualTo("p2"));
}

[Test]
public void AssignSlot_fails_during_battle()
{
    var mod = new RunMod { IsMultiplayer = true, Phase = ERunPhase.Battle };
    mod.PlayerStates[0].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[1].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[2].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[3].SetActiveCharacter(new CharacterInstance());
    mod.AddPlayerController(new PlayerController("owner", "Owner", true));
    mod.AddPlayerController(new PlayerController("p2", "Player2", false));
    var controller = new RunController(mod);

    var result = controller.AssignSlot(0, "p2");

    Assert.That(result, Is.False);
}

[Test]
public void AssignSlot_fails_for_unknown_player()
{
    var mod = new RunMod { IsMultiplayer = true, Phase = ERunPhase.Event };
    mod.PlayerStates[0].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[1].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[2].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[3].SetActiveCharacter(new CharacterInstance());
    mod.AddPlayerController(new PlayerController("owner", "Owner", true));
    var controller = new RunController(mod);

    var result = controller.AssignSlot(0, "ghost");

    Assert.That(result, Is.False);
}

[Test]
public void AllSlotsAssigned_checks_all_four()
{
    var mod = new RunMod { IsMultiplayer = true };
    var controller = new RunController(mod);
    mod.AddPlayerController(new PlayerController("owner", "Owner", true));

    Assert.That(controller.AllSlotsAssigned(), Is.False);

    for (var i = 0; i < 4; i++)
        mod.AssignSlotInternal(i, "owner");

    Assert.That(controller.AllSlotsAssigned(), Is.True);
}

[Test]
public void OnPlayerDisconnected_non_owner_during_battle_transfers_to_owner()
{
    var mod = new RunMod { IsMultiplayer = true, Phase = ERunPhase.Battle };
    mod.PlayerStates[0].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[1].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[2].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[3].SetActiveCharacter(new CharacterInstance());
    mod.AddPlayerController(new PlayerController("owner", "Owner", true));
    mod.AddPlayerController(new PlayerController("p2", "Player2", false));
    mod.AssignSlotInternal(0, "owner");
    mod.AssignSlotInternal(1, "p2");
    mod.AssignSlotInternal(2, "owner");
    mod.AssignSlotInternal(3, "owner");
    var controller = new RunController(mod);

    controller.OnPlayerDisconnected("p2");

    Assert.That(mod.SlotOwnership[1], Is.EqualTo("owner"));
}

[Test]
public void OnPlayerDisconnected_owner_during_battle_abandons_run()
{
    var mod = new RunMod { IsMultiplayer = true, Phase = ERunPhase.Battle };
    mod.PlayerStates[0].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[1].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[2].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[3].SetActiveCharacter(new CharacterInstance());
    mod.AddPlayerController(new PlayerController("owner", "Owner", true));
    mod.AddPlayerController(new PlayerController("p2", "Player2", false));
    var controller = new RunController(mod);

    controller.OnPlayerDisconnected("owner");

    Assert.That(mod.Phase, Is.EqualTo(ERunPhase.Finished));
}

[Test]
public void SwapSlotOwnership_swaps_in_non_battle_phase()
{
    var mod = new RunMod { IsMultiplayer = true, Phase = ERunPhase.Event };
    mod.PlayerStates[0].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[1].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[2].SetActiveCharacter(new CharacterInstance());
    mod.PlayerStates[3].SetActiveCharacter(new CharacterInstance());
    mod.AddPlayerController(new PlayerController("owner", "Owner", true));
    mod.AddPlayerController(new PlayerController("p2", "Player2", false));
    mod.AssignSlotInternal(0, "p2");
    mod.AssignSlotInternal(1, "owner");
    var controller = new RunController(mod);

    var result = controller.SwapSlotOwnership(slotA: 0, slotB: 1, initiatorId: "p2", targetId: "owner");

    Assert.That(result, Is.True);
    Assert.That(mod.SlotOwnership[0], Is.EqualTo("owner"));
    Assert.That(mod.SlotOwnership[1], Is.EqualTo("p2"));
}

[Test]
public void SwapSlotOwnership_fails_during_battle()
{
    var mod = new RunMod { IsMultiplayer = true, Phase = ERunPhase.Battle };
    mod.AddPlayerController(new PlayerController("owner", "Owner", true));
    mod.AddPlayerController(new PlayerController("p2", "Player2", false));
    mod.AssignSlotInternal(0, "p2");
    mod.AssignSlotInternal(1, "owner");
    var controller = new RunController(mod);

    var result = controller.SwapSlotOwnership(0, 1, "p2", "owner");

    Assert.That(result, Is.False);
}

[Test]
public void GetSlotsForPlayer_returns_correct_slots()
{
    var mod = new RunMod { IsMultiplayer = true };
    mod.AssignSlotInternal(0, "p1");
    mod.AssignSlotInternal(1, "p2");
    mod.AssignSlotInternal(2, "p1");
    mod.AssignSlotInternal(3, "p2");
    var controller = new RunController(mod);

    var p1Slots = controller.GetSlotsForPlayer("p1");
    var p2Slots = controller.GetSlotsForPlayer("p2");

    Assert.That(p1Slots, Is.EquivalentTo(new[] { 0, 2 }));
    Assert.That(p2Slots, Is.EquivalentTo(new[] { 1, 3 }));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunControllerTests"`

Expected: Some new tests FAIL — 方法尚未实现

- [ ] **Step 3: 追加控制权方法到 RunController**

在 `RunController` 类的 `#region 流程控制` 之后、`#region 持久化` 之前插入：

```csharp
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
            {
                Model.AssignSlotInternal(slot, ownerId);
            }
        }
    }
    else
    {
        foreach (var slot in GetSlotsForPlayer(playerId))
        {
            Model.AssignSlotInternal(slot, "");
        }
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunControllerTests"`

Expected: All tests PASS (16 original + 10 new = 26)

- [ ] **Step 5: Commit**

```bash
git add Src/mod/run/RunController.cs Tests/kemo_card.Ui.Tests/Run/RunControllerTests.cs
git commit -m "feat: 实现 RunController 控制权管理方法"
```

---

### Task 12: RunController 战斗管理与快照回滚

**Files:**
- Modify: `Src/mod/run/RunController.cs`
- Modify: `Tests/kemo_card.Ui.Tests/Run/RunControllerTests.cs`

- [ ] **Step 1: 追加战斗相关测试**

在 `RunControllerTests` 文件头部追加 `using KemoCard.Ui.Tests.Combat;`，然后在类末尾追加：

```csharp
[Test]
public void StartBattle_fails_when_party_not_valid()
{
    var mod = new RunMod { Phase = ERunPhase.Reward, IsMultiplayer = false };
    mod.SlotOwnership[0] = "local";
    mod.SlotOwnership[1] = "local";
    mod.SlotOwnership[2] = "local";
    mod.SlotOwnership[3] = "local";
    mod.AddPlayerController(new PlayerController("local", "Player", true));
    var controller = new RunController(mod);

    Assert.Throws<InvalidOperationException>(() =>
        controller.StartBattle(CombatTestHelper.CreateFullRegistry(), new HostRng(1, "battle"), 1));
}

[Test]
public void StartBattle_fails_when_slots_not_all_assigned()
{
    var mod = new RunMod { Phase = ERunPhase.Reward, IsMultiplayer = true };
    for (var i = 0; i < 4; i++)
    {
        var c = new CharacterInstance(new CharacterDto { Id = $"hero_{i}", Cards = [] }, $"inst-{i}");
        mod.AddToCharacterPool(c);
        mod.PlayerStates[i].SetActiveCharacter(c);
    }
    mod.AddPlayerController(new PlayerController("owner", "Owner", true));
    mod.AddPlayerController(new PlayerController("p2", "Player2", false));
    mod.AssignSlotInternal(0, "owner");
    mod.AssignSlotInternal(1, "p2");
    mod.AssignSlotInternal(2, "owner");
    var controller = new RunController(mod);

    Assert.Throws<InvalidOperationException>(() =>
        controller.StartBattle(CombatTestHelper.CreateFullRegistry(), new HostRng(1, "battle"), 1));
}

[Test]
public void StartBattle_creates_snapshot_and_changes_phase()
{
    var mod = new RunMod { Phase = ERunPhase.Reward, IsMultiplayer = false };
    for (var i = 0; i < 4; i++)
    {
        var c = new CharacterInstance(new CharacterDto { Id = $"hero_{i}", Cards = [] }, $"inst-{i}");
        mod.AddToCharacterPool(c);
        mod.PlayerStates[i].SetActiveCharacter(c);
    }
    mod.AddPlayerController(new PlayerController("local", "Player", true));
    for (var i = 0; i < 4; i++)
        mod.AssignSlotInternal(i, "local");
    var controller = new RunController(mod);

    var simulation = controller.StartBattle(
        CombatTestHelper.CreateFullRegistry(), new HostRng(1, "battle"), 1);

    Assert.That(simulation, Is.Not.Null);
    Assert.That(mod.Phase, Is.EqualTo(ERunPhase.Battle));
}

[Test]
public void EndBattle_victory_sets_phase_to_ring_end()
{
    var mod = new RunMod { Phase = ERunPhase.BattleEnd, IsMultiplayer = false };
    var controller = new RunController(mod);

    controller.EndBattle(won: true);

    Assert.That(mod.Phase, Is.EqualTo(ERunPhase.RingEnd));
}

[Test]
public void EndBattle_defeat_rolls_back_to_event()
{
    var mod = new RunMod { Phase = ERunPhase.Reward, IsMultiplayer = false, SharedGold = 500 };
    for (var i = 0; i < 4; i++)
    {
        var c = new CharacterInstance(new CharacterDto { Id = $"hero_{i}", Cards = [] }, $"inst-{i}");
        mod.AddToCharacterPool(c);
        mod.PlayerStates[i].SetActiveCharacter(c);
    }
    mod.AddPlayerController(new PlayerController("local", "Player", true));
    for (var i = 0; i < 4; i++)
        mod.AssignSlotInternal(i, "local");
    var controller = new RunController(mod);

    controller.StartBattle(CombatTestHelper.CreateFullRegistry(), new HostRng(1, "battle"), 1);
    mod.SharedGold = 100;
    controller.EndBattle(won: false);

    Assert.That(mod.Phase, Is.EqualTo(ERunPhase.Event));
    Assert.That(mod.SharedGold, Is.EqualTo(500));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunControllerTests" --filter "Name~Battle"

Expected: Some new tests FAIL — 方法尚未实现

- [ ] **Step 3: 追加战斗管理方法到 RunController**

在 `RunController` 类的控制权管理 region 之后，持久化 region 之前插入：

```csharp
#region 战斗管理

public Combat.Combat.Runtime.CombatSimulation StartBattle(
    KemoCard.Frame.Content.GameDefinitionRegistry definitions,
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
    Model.Phase = ERunPhase.Battle;

    var activeParty = Model.ActiveParty;
    var characterInstances = new List<Combat.CharacterInstance>();
    for (var i = 0; i < activeParty.Length; i++)
    {
        if (activeParty[i] != null)
            characterInstances.Add(activeParty[i]!);
    }

    var party = characterInstances
        .Select(c => Combat.CharacterBattleInstance.CreateForTests(
            c.DefinitionId,
            c.ComputeAttributeMap(definitions)))
        .ToArray();

    var enemy = new Combat.Runtime.EnemyUnit(
        "enemy-default",
        "slime",
        new Dictionary<string, float>(StringComparer.Ordinal) { ["max_health"] = 10f });

    var playerTeam = new Combat.Runtime.PlayerTeamState(party, sharedMaxHp: 40);
    var enemyTeam = new Combat.Runtime.EnemyTeamState([enemy]);
    var ruleEngine = new Combat.Rules.CombatRuleEngine([]);

    return new Combat.Runtime.CombatSimulation(
        playerTeam,
        enemyTeam,
        ruleEngine,
        definitions,
        initialPhase: Combat.StateMachine.ECombatPhase.Player,
        runSeed: runSeed);
}

public void EndBattle(bool won)
{
    if (Model.Phase != ERunPhase.Battle && Model.Phase != ERunPhase.BattleEnd)
        throw new InvalidOperationException("当前不在战斗中。");

    if (won)
    {
        Model.AddBattleRecord(new BattleRecordDto
        {
            BattleId = $"battle_r{Model.CurrentRing}",
            Won = true,
            TurnsUsed = 0,
            DamageDealt = 0,
            DamageTaken = 0,
        });

        Model.Phase = ERunPhase.RingEnd;
    }
    else
    {
        if (_battleSnapshot != null)
        {
            Model.RestoreFrom(_battleSnapshot);
            _battleSnapshot = null;
        }

        Model.Phase = ERunPhase.Event;
    }
}

#endregion
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunControllerTests"`

Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add Src/mod/run/RunController.cs Tests/kemo_card.Ui.Tests/Run/RunControllerTests.cs
git commit -m "feat: 实现 RunController 战斗管理与快照回滚"
```

---

### Task 13: 集成测试

**Files:**
- Create: `Tests/kemo_card.Ui.Tests/Run/RunIntegrationTests.cs`

- [ ] **Step 1: 编写集成测试**

```csharp
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Save;
using KemoCard.Ui.Tests.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunIntegrationTests
{
    [Test]
    public void Full_run_cycle_create_event_battle_save_load()
    {
        var mod = new RunMod();
        var controller = new RunController(mod);
        var rng = new HostRng(42, "integration");
        var tempDir = Path.Combine(Path.GetTempPath(), $"run_int_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var candidates = new List<CharacterDto>
            {
                new() { Id = "hero_a", Cards = [] },
                new() { Id = "hero_b", Cards = [] },
                new() { Id = "hero_c", Cards = [] },
                new() { Id = "hero_d", Cards = [] },
            };

            var dto = controller.CreateRun(rng, candidates, isMultiplayer: false);
            Assert.That(dto.Phase, Is.EqualTo(ERunPhase.Event));
            Assert.That(dto.CurrentRing, Is.EqualTo(1));

            for (var i = 0; i < 4; i++)
            {
                var character = new CharacterInstance(new CharacterDto { Id = $"hero_{i}", Cards = [] }, $"inst-{i}");
                controller.AddToCharacterPool(character);
                controller.SetActiveCharacter(i, poolIndex: i);
            }

            controller.AddGold(slotIndex: 0, amount: 200);
            Assert.That(controller.GetGold(), Is.EqualTo(200));

            Assert.That(controller.ValidateParty(), Is.True);

            mod.Phase = ERunPhase.Reward;
            var simulation = controller.StartBattle(
                CombatTestHelper.CreateFullRegistry(), rng, 1);
            Assert.That(mod.Phase, Is.EqualTo(ERunPhase.Battle));
            Assert.That(simulation, Is.Not.Null);

            mod.Phase = ERunPhase.BattleEnd;
            controller.EndBattle(won: true);
            Assert.That(mod.Phase, Is.EqualTo(ERunPhase.RingEnd));

            controller.NextRing();
            Assert.That(mod.Phase, Is.EqualTo(ERunPhase.Event));
            Assert.That(mod.CurrentRing, Is.EqualTo(2));

            var saveService = new RunSaveService(tempDir);
            controller.Save(saveService);
            Assert.That(saveService.Exists, Is.True);

            var loaded = saveService.LoadOrDefault();
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.RunId, Is.EqualTo(mod.RunId));
            Assert.That(loaded.CurrentRing, Is.EqualTo(2));
            Assert.That(loaded.SharedGold, Is.EqualTo(200));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void Battle_defeat_rollback_preserves_pre_battle_state()
    {
        var mod = new RunMod();
        var controller = new RunController(mod);
        var rng = new HostRng(99, "rollback");

        controller.CreateRun(rng, [], isMultiplayer: false);
        mod.Phase = ERunPhase.Reward;

        for (var i = 0; i < 4; i++)
        {
            var character = new CharacterInstance(new CharacterDto { Id = $"hero_{i}", Cards = [] }, $"inst-{i}");
            controller.AddToCharacterPool(character);
            controller.SetActiveCharacter(i, poolIndex: i);
        }

        controller.AddGold(slotIndex: 0, amount: 300);
        var goldBefore = controller.GetGold();
        controller.AddCard("card.test");

        controller.StartBattle(CombatTestHelper.CreateFullRegistry(), rng, 1);
        controller.SpendGold(slotIndex: 0, amount: 100);
        controller.RemoveCard("card.test");

        controller.EndBattle(won: false);

        Assert.That(controller.GetGold(), Is.EqualTo(goldBefore));
        Assert.That(mod.CardCollection, Does.Contain("card.test"));
    }
}
```

- [ ] **Step 2: Run integration tests**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~RunIntegrationTests"`

Expected: 2 tests PASS

- [ ] **Step 3: Commit**

```bash
git add Tests/kemo_card.Ui.Tests/Run/RunIntegrationTests.cs
git commit -m "test: 添加 Run 集成测试"
```

---

### Task 14: 全量测试与验证

- [ ] **Step 1: 运行所有 Run 模块测试**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~Run"`

Expected: All Run tests PASS

- [ ] **Step 2: 运行全量回归测试**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj`

Expected: All existing tests still PASS

- [ ] **Step 3: Commit**

```bash
git commit --allow-empty -m "chore: 验证 Run Mod 全量测试通过"
```
