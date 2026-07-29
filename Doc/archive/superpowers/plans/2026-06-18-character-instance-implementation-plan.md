# 角色实例与战斗实例首期实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，按任务逐步实现。步骤使用 checkbox（`- [ ]`）追踪。

**Goal:** 实现 `DeckPreset`、`CharacterInstance`、`HandSlot`、`CharacterBattleInstance` 四个核心类及直接依赖（`CharacterAttributes`、`CardStatBlockDto`），覆盖动态卡组管理与进战斗牌区初始化；不含 Buff 运行时、局内存档、战斗流程。

**Architecture:** 业务运行时放在 `Src/mod/combat/`；卡牌属性定义扩展在 `Src/frame/content/definitions/`。`CharacterInstance` 管 Run 期卡组；`CharacterBattleInstance.TryCreate` 从当前卡组生成洗牌牌库与空手牌。构筑校验通过方法参数 `IReadOnlySet<string> obtainedCardIds` 传入，不引入 `ObtainedCardPool` 类型。

**Tech Stack:** Godot 4.6.1 Mono + .NET 8 + C# 12 + NUnit 4

**设计规格:** [`Doc/superpowers/specs/2026-06-18-character-instance-design.md`](../specs/2026-06-18-character-instance-design.md)

---

## 文件结构

| 路径 | 职责 |
|------|------|
| `Src/frame/content/definitions/CardStatBlockDto.cs` | 卡牌属性贡献 JSON DTO |
| `Src/frame/content/definitions/CardDto.cs` | 增加可选 `stats` 字段 |
| `Src/mod/combat/CombatConstants.cs` | `MaxDecksPerCharacter`、`MaxCardsPerDeck`、`HandSlotCount` |
| `Src/mod/combat/CharacterAttributes.cs` | 属性值对象与 `+` 运算符 |
| `Src/mod/combat/DeckValidationResult.cs` | 构筑校验结果 |
| `Src/mod/combat/DeckPreset.cs` | 单套卡组 |
| `Src/mod/combat/CharacterInstance.cs` | 角色实例 |
| `Src/mod/combat/HandSlotEffectRef.cs` | 槽位效果占位（后续换 BuffInstance） |
| `Src/mod/combat/HandSlot.cs` | 手牌单槽 |
| `Src/mod/combat/CardRuntimeEntry.cs` | 战斗中卡牌运行时条目 |
| `Src/mod/combat/CharacterBattleInstance.cs` | 角色战斗实例 |
| `Tests/kemo_card.Ui.Tests/Combat/DeckPresetTests.cs` | DeckPreset 测试 |
| `Tests/kemo_card.Ui.Tests/Combat/CharacterInstanceTests.cs` | CharacterInstance 测试 |
| `Tests/kemo_card.Ui.Tests/Combat/HandSlotTests.cs` | HandSlot 测试 |
| `Tests/kemo_card.Ui.Tests/Combat/CombatTestHelper.cs` | 测试用 Registry 构建辅助 |

| `Tests/kemo_card.Ui.Tests/Combat/CharacterBattleInstanceTests.cs` | 战斗实例测试 |

---

### Task 0：测试辅助 `CombatTestHelper`

**Files:**
- Create: `Tests/kemo_card.Ui.Tests/Combat/CombatTestHelper.cs`

- [ ] **Step 1: 创建辅助类**

`Tests/kemo_card.Ui.Tests/Combat/CombatTestHelper.cs`：

```csharp
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Ui.Tests.Combat;

internal static class CombatTestHelper
{
    public static GameDefinitionRegistry CreateRegistry(params CardDto[] cards)
    {
        var cardDict = cards.ToDictionary(card => card.Id, StringComparer.Ordinal);
        var definitions = new ModDefinitionsBundle(
            ModDefinitionsBundle.Empty.Characters,
            ModDefinitionsBundle.Empty.Enemies,
            ModDefinitionsBundle.Empty.Battles,
            ModDefinitionsBundle.Empty.Events,
            ModDefinitionsBundle.Empty.Items,
            cardDict,
            ModDefinitionsBundle.Empty.Skills,
            ModDefinitionsBundle.Empty.Buffs,
            ModDefinitionsBundle.Empty.Effects);

        var bundle = new ModContentBundle(
            ModId: "test.mod",
            Characters: [],
            Enemies: [],
            Battles: [],
            Events: [],
            Cards: cardDict.Keys.ToList(),
            Items: [],
            Skills: [],
            Buffs: [],
            Effects: [],
            Definitions: definitions);

        var registry = new GameDefinitionRegistry();
        registry.Rebuild([bundle], out _);
        return registry;
    }
}
```

- [ ] **Step 2: 提交**

```powershell
git add Tests/kemo_card.Ui.Tests/Combat/CombatTestHelper.cs
git commit -m "test(combat): 添加 CombatTestHelper 注册表构建辅助"
```

---

**Files:**
- Create: `Src/frame/content/definitions/CardStatBlockDto.cs`
- Modify: `Src/frame/content/definitions/CardDto.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CardStatBlockTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Text.Json;
using KemoCard.Frame.Content.Definitions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CardStatBlockTests
{
    [Test]
    public void CardDto_deserializes_stats_block()
    {
        const string json = """
            {
              "id": "strike",
              "displayNameId": "card.strike",
              "costType": "Energy",
              "cost": 1,
              "cardType": "Physics",
              "targetSide": "Enemy",
              "targetScope": "Single",
              "rarity": "Common",
              "stats": {
                "hpCap": 5,
                "physicalAttack": 2,
                "maxEnergy": 3,
                "initialEnergy": 1
              }
            }
            """;

        var card = JsonSerializer.Deserialize<CardDto>(json, ContentDefinitionJson.Options)!;

        Assert.That(card.Stats, Is.Not.Null);
        Assert.That(card.Stats!.HpCap, Is.EqualTo(5));
        Assert.That(card.Stats.PhysicalAttack, Is.EqualTo(2));
        Assert.That(card.Stats.MaxEnergy, Is.EqualTo(3));
        Assert.That(card.Stats.InitialEnergy, Is.EqualTo(1));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
Set-Location "D:\Godot_v4.6.1-stable_mono_win64\Projects\kemo_card"
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~CardStatBlockTests
```

Expected: **FAIL**（`Stats` 不存在或反序列化为 null）

- [ ] **Step 3: 实现 DTO**

`Src/frame/content/definitions/CardStatBlockDto.cs`：

```csharp
using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class CardStatBlockDto
{
    [JsonPropertyName("hpCap")]
    public int HpCap { get; init; }

    [JsonPropertyName("physicalAttack")]
    public int PhysicalAttack { get; init; }

    [JsonPropertyName("physicalDefense")]
    public int PhysicalDefense { get; init; }

    [JsonPropertyName("magicAttack")]
    public int MagicAttack { get; init; }

    [JsonPropertyName("magicDefense")]
    public int MagicDefense { get; init; }

    [JsonPropertyName("healPower")]
    public int HealPower { get; init; }

    [JsonPropertyName("physicalShield")]
    public int PhysicalShield { get; init; }

    [JsonPropertyName("magicShield")]
    public int MagicShield { get; init; }

    [JsonPropertyName("damageScale")]
    public int DamageScale { get; init; }

    [JsonPropertyName("damageTakenScale")]
    public int DamageTakenScale { get; init; }

    [JsonPropertyName("taunt")]
    public int Taunt { get; init; }

    [JsonPropertyName("drawCount")]
    public int DrawCount { get; init; }

    [JsonPropertyName("maxEnergy")]
    public int MaxEnergy { get; init; }

    [JsonPropertyName("initialEnergy")]
    public int InitialEnergy { get; init; }
}
```

在 `CardDto.cs` 末尾属性区增加：

```csharp
    [JsonPropertyName("stats")]
    public CardStatBlockDto? Stats { get; init; }
```

- [ ] **Step 4: 运行测试确认通过**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~CardStatBlockTests
```

Expected: **PASS**

- [ ] **Step 5: 提交**

```powershell
git add Src/frame/content/definitions/CardStatBlockDto.cs Src/frame/content/definitions/CardDto.cs Tests/kemo_card.Ui.Tests/Combat/CardStatBlockTests.cs
git commit -m "feat(content): 为 CardDto 增加 stats 属性贡献块"
```

---

### Task 2：`CharacterAttributes` 与 `CombatConstants`

**Files:**
- Create: `Src/mod/combat/CombatConstants.cs`
- Create: `Src/mod/combat/CharacterAttributes.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CharacterAttributesTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CharacterAttributesTests
{
    [Test]
    public void Plus_sums_all_fields()
    {
        var a = CharacterAttributes.FromCardStats(new CardStatBlockDto { HpCap = 3, PhysicalAttack = 2 });
        var b = CharacterAttributes.FromCardStats(new CardStatBlockDto { HpCap = 5, PhysicalAttack = 1 });

        var sum = a + b;

        Assert.That(sum.HpCap, Is.EqualTo(8));
        Assert.That(sum.PhysicalAttack, Is.EqualTo(3));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~CharacterAttributesTests
```

Expected: **FAIL**

- [ ] **Step 3: 实现**

`Src/mod/combat/CombatConstants.cs`：

```csharp
namespace KemoCard.Mod.Combat;

public static class CombatConstants
{
    public const int MaxDecksPerCharacter = 10;
    public const int MaxCardsPerDeck = 10;
    public const int HandSlotCount = 5;
}
```

`Src/mod/combat/CharacterAttributes.cs`：

```csharp
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat;

public readonly record struct CharacterAttributes(
    int HpCap,
    int PhysicalAttack,
    int PhysicalDefense,
    int MagicAttack,
    int MagicDefense,
    int HealPower,
    int PhysicalShield,
    int MagicShield,
    int DamageScale,
    int DamageTakenScale,
    int Taunt,
    int DrawCount,
    int MaxEnergy,
    int InitialEnergy)
{
    public static CharacterAttributes Zero { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static CharacterAttributes FromCardStats(CardStatBlockDto stats) => new(
        stats.HpCap,
        stats.PhysicalAttack,
        stats.PhysicalDefense,
        stats.MagicAttack,
        stats.MagicDefense,
        stats.HealPower,
        stats.PhysicalShield,
        stats.MagicShield,
        stats.DamageScale,
        stats.DamageTakenScale,
        stats.Taunt,
        stats.DrawCount,
        stats.MaxEnergy,
        stats.InitialEnergy);

    public static CharacterAttributes operator +(CharacterAttributes left, CharacterAttributes right) => new(
        left.HpCap + right.HpCap,
        left.PhysicalAttack + right.PhysicalAttack,
        left.PhysicalDefense + right.PhysicalDefense,
        left.MagicAttack + right.MagicAttack,
        left.MagicDefense + right.MagicDefense,
        left.HealPower + right.HealPower,
        left.PhysicalShield + right.PhysicalShield,
        left.MagicShield + right.MagicShield,
        left.DamageScale + right.DamageScale,
        left.DamageTakenScale + right.DamageTakenScale,
        left.Taunt + right.Taunt,
        left.DrawCount + right.DrawCount,
        left.MaxEnergy + right.MaxEnergy,
        left.InitialEnergy + right.InitialEnergy);
}
```

- [ ] **Step 4: 运行测试确认通过**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~CharacterAttributesTests
```

Expected: **PASS**

- [ ] **Step 5: 提交**

```powershell
git add Src/mod/combat/CombatConstants.cs Src/mod/combat/CharacterAttributes.cs Tests/kemo_card.Ui.Tests/Combat/CharacterAttributesTests.cs
git commit -m "feat(combat): 添加 CharacterAttributes 值对象"
```

---

### Task 3：`DeckPreset` 与 `DeckValidationResult`

**Files:**
- Create: `Src/mod/combat/DeckValidationResult.cs`
- Create: `Src/mod/combat/DeckPreset.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/DeckPresetTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class DeckPresetTests
{
    [Test]
    public void CreateWithExclusiveCards_takes_first_ten()
    {
        var definition = new CharacterDto
        {
            Id = "hero",
            Cards = Enumerable.Range(1, 12).Select(i => $"card_{i}").ToList(),
        };

        var deck = DeckPreset.CreateWithExclusiveCards(definition);

        Assert.That(deck.CardIds, Has.Count.EqualTo(10));
        Assert.That(deck.CardIds[0], Is.EqualTo("card_1"));
        Assert.That(deck.CardIds[9], Is.EqualTo("card_10"));
    }

    [Test]
    public void Validate_rejects_card_not_in_buildable_pool()
    {
        var deck = new DeckPreset("d1", null, ["unknown"]);
        var buildable = new HashSet<string>(StringComparer.Ordinal) { "strike" };

        var result = deck.Validate(buildable);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.InvalidCardIds, Does.Contain("unknown"));
    }

    [Test]
    public void TryAddCard_rejects_duplicate()
    {
        var deck = new DeckPreset("d1", null, ["a"]);
        var buildable = new HashSet<string>(StringComparer.Ordinal) { "a", "b" };

        Assert.That(deck.TryAddCard("b", buildable), Is.True);
        Assert.That(deck.TryAddCard("b", buildable), Is.False);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~DeckPresetTests
```

Expected: **FAIL**

- [ ] **Step 3: 实现**

`Src/mod/combat/DeckValidationResult.cs`：

```csharp
namespace KemoCard.Mod.Combat;

public sealed class DeckValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> InvalidCardIds { get; init; } = [];

    public static DeckValidationResult Ok() => new() { IsValid = true };

    public static DeckValidationResult Fail(IReadOnlyList<string> invalidCardIds) => new()
    {
        IsValid = false,
        InvalidCardIds = invalidCardIds,
    };
}
```

`Src/mod/combat/DeckPreset.cs`：

```csharp
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat;

public sealed class DeckPreset
{
    public string DeckId { get; }
    public string? DisplayName { get; private set; }
    private readonly List<string> _cardIds;

    public IReadOnlyList<string> CardIds => _cardIds;

    public DeckPreset(string deckId, string? displayName, IEnumerable<string>? cardIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deckId);
        DeckId = deckId;
        DisplayName = displayName;
        _cardIds = cardIds?.ToList() ?? [];
    }

    public static DeckPreset CreateWithExclusiveCards(CharacterDto definition, string? deckId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var cards = definition.Cards.Take(CombatConstants.MaxCardsPerDeck).ToList();
        return new DeckPreset(deckId ?? Guid.NewGuid().ToString("N"), null, cards);
    }

    public bool TryAddCard(string cardId, IReadOnlySet<string> buildableCardIds)
    {
        if (_cardIds.Count >= CombatConstants.MaxCardsPerDeck)
            return false;
        if (_cardIds.Contains(cardId, StringComparer.Ordinal))
            return false;
        if (!buildableCardIds.Contains(cardId))
            return false;

        _cardIds.Add(cardId);
        return true;
    }

    public bool TryRemoveCard(string cardId)
    {
        return _cardIds.Remove(cardId);
    }

    public DeckValidationResult Validate(IReadOnlySet<string> buildableCardIds)
    {
        if (_cardIds.Count > CombatConstants.MaxCardsPerDeck)
            return DeckValidationResult.Fail(_cardIds.ToList());

        var invalid = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cardId in _cardIds)
        {
            if (!seen.Add(cardId))
                invalid.Add(cardId);
            else if (!buildableCardIds.Contains(cardId))
                invalid.Add(cardId);
        }

        return invalid.Count == 0
            ? DeckValidationResult.Ok()
            : DeckValidationResult.Fail(invalid);
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~DeckPresetTests
```

Expected: **PASS**

- [ ] **Step 5: 提交**

```powershell
git add Src/mod/combat/DeckValidationResult.cs Src/mod/combat/DeckPreset.cs Tests/kemo_card.Ui.Tests/Combat/DeckPresetTests.cs
git commit -m "feat(combat): 添加 DeckPreset 与构筑校验"
```

---

### Task 4：`CharacterInstance`

**Files:**
- Create: `Src/mod/combat/CharacterInstance.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CharacterInstanceTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CharacterInstanceTests
{
    private static CharacterDto CreateDefinition(params string[] exclusiveCards) => new()
    {
        Id = "kemo",
        Cards = exclusiveCards.ToList(),
    };

    [Test]
    public void Constructor_from_dto_starts_with_one_exclusive_deck()
    {
        var instance = new CharacterInstance(CreateDefinition("c1", "c2"));

        Assert.That(instance.Decks, Has.Count.EqualTo(1));
        Assert.That(instance.Decks[0].CardIds, Is.EquivalentTo(new[] { "c1", "c2" }));
        Assert.That(instance.CurrentDeckIndex, Is.EqualTo(0));
    }

    [Test]
    public void TryCreateDeck_appends_until_cap()
    {
        var instance = new CharacterInstance(CreateDefinition("c1"));

        for (var i = 1; i < CombatConstants.MaxDecksPerCharacter; i++)
            Assert.That(instance.TryCreateDeck(), Is.True);

        Assert.That(instance.TryCreateDeck(), Is.False);
        Assert.That(instance.Decks, Has.Count.EqualTo(CombatConstants.MaxDecksPerCharacter));
    }

    [Test]
    public void TryEditDeck_blocked_when_locked()
    {
        var instance = new CharacterInstance(CreateDefinition("c1"));
        instance.SetDeckLocked(true);

        var edited = instance.TryEditDeck(0, deck => deck.TryAddCard("x", new HashSet<string> { "x" }));

        Assert.That(edited, Is.False);
    }

    [Test]
    public void ComputeAttributes_sums_current_deck_card_stats()
    {
        var registry = CombatTestHelper.CreateRegistry(new CardDto
        {
            Id = "strike",
            Stats = new CardStatBlockDto { HpCap = 4, PhysicalAttack = 2 },
        });
        var instance = new CharacterInstance(CreateDefinition("strike"));

        var attrs = instance.ComputeAttributes(registry);

        Assert.That(attrs.HpCap, Is.EqualTo(4));
        Assert.That(attrs.PhysicalAttack, Is.EqualTo(2));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~CharacterInstanceTests
```

Expected: **FAIL**

- [ ] **Step 3: 实现**

`Src/mod/combat/CharacterInstance.cs`：

```csharp
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat;

public sealed class CharacterInstance
{
    private readonly List<DeckPreset> _decks = [];

    public string InstanceId { get; }
    public string DefinitionId { get; private set; }
    public CharacterDto? Definition { get; private set; }
    public IReadOnlyList<DeckPreset> Decks => _decks;
    public int CurrentDeckIndex { get; private set; }
    public bool IsDeckLocked { get; private set; }

    public CharacterInstance(CharacterDto definition, string? instanceId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        InstanceId = instanceId ?? Guid.NewGuid().ToString("N");
        BindDefinition(definition);
        _decks.Add(DeckPreset.CreateWithExclusiveCards(definition));
        CurrentDeckIndex = 0;
    }

    public CharacterInstance()
    {
        InstanceId = Guid.NewGuid().ToString("N");
        DefinitionId = "";
    }

    public void BindDefinition(CharacterDto definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        DefinitionId = definition.Id;
    }

    public void SetDeckLocked(bool locked) => IsDeckLocked = locked;

    public bool TryCreateDeck()
    {
        if (IsDeckLocked || Definition is null)
            return false;
        if (_decks.Count >= CombatConstants.MaxDecksPerCharacter)
            return false;

        _decks.Add(DeckPreset.CreateWithExclusiveCards(Definition));
        return true;
    }

    public bool TryEditDeck(int deckIndex, Action<DeckPreset> edit)
    {
        if (IsDeckLocked)
            return false;
        if (deckIndex < 0 || deckIndex >= _decks.Count)
            return false;

        edit(_decks[deckIndex]);
        return true;
    }

    public bool TrySetCurrentDeck(int index)
    {
        if (IsDeckLocked)
            return false;
        if (index < 0 || index >= _decks.Count)
            return false;

        CurrentDeckIndex = index;
        return true;
    }

    public DeckPreset? GetCurrentDeck()
    {
        if (_decks.Count == 0 || CurrentDeckIndex < 0 || CurrentDeckIndex >= _decks.Count)
            return null;
        return _decks[CurrentDeckIndex];
    }

    public HashSet<string> GetBuildableCardIds(IReadOnlySet<string> obtainedCardIds)
    {
        var set = new HashSet<string>(obtainedCardIds, StringComparer.Ordinal);
        if (Definition is not null)
        {
            foreach (var cardId in Definition.Cards)
                set.Add(cardId);
        }
        return set;
    }

    public DeckValidationResult ValidateCurrentDeck(IReadOnlySet<string> obtainedCardIds)
    {
        var deck = GetCurrentDeck();
        if (deck is null)
            return DeckValidationResult.Fail([]);
        return deck.Validate(GetBuildableCardIds(obtainedCardIds));
    }

    public CharacterAttributes ComputeAttributes(GameDefinitionRegistry definitions)
    {
        var deck = GetCurrentDeck();
        if (deck is null)
            return CharacterAttributes.Zero;

        var total = CharacterAttributes.Zero;
        foreach (var cardId in deck.CardIds)
        {
            if (!definitions.Store.TryGetCard(cardId, out var card) || card.Stats is null)
                continue;
            total += CharacterAttributes.FromCardStats(card.Stats);
        }
        return total;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~CharacterInstanceTests
```

Expected: **PASS**

- [ ] **Step 5: 提交**

```powershell
git add Src/mod/combat/CharacterInstance.cs Tests/kemo_card.Ui.Tests/Combat/CharacterInstanceTests.cs
git commit -m "feat(combat): 添加 CharacterInstance 动态卡组管理"
```

---

### Task 5：`HandSlot` 与 `HandSlotEffectRef`

**Files:**
- Create: `Src/mod/combat/HandSlotEffectRef.cs`
- Create: `Src/mod/combat/HandSlot.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/HandSlotTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Mod.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class HandSlotTests
{
    [Test]
    public void PlaceCard_and_ClearCard()
    {
        var slot = new HandSlot(0);

        slot.PlaceCard("strike", "rt-1");
        Assert.That(slot.CardId, Is.EqualTo("strike"));
        Assert.That(slot.RuntimeInstanceId, Is.EqualTo("rt-1"));

        slot.ClearCard();
        Assert.That(slot.IsEmpty, Is.True);
    }

    [Test]
    public void TryAddSlotEffect_stores_buff_ref_for_future_pipeline()
    {
        var slot = new HandSlot(2);
        var added = slot.TryAddSlotEffect("poison", new Dictionary<string, object> { ["stacks"] = 1 });

        Assert.That(added, Is.True);
        Assert.That(slot.SlotEffects, Has.Count.EqualTo(1));
        Assert.That(slot.SlotEffects[0].BuffId, Is.EqualTo("poison"));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~HandSlotTests
```

Expected: **FAIL**

- [ ] **Step 3: 实现**

`Src/mod/combat/HandSlotEffectRef.cs`：

```csharp
namespace KemoCard.Mod.Combat;

/// <summary>
/// 手牌槽位效果占位；后续里程碑替换为 BuffInstance 并接入结算管线。
/// </summary>
public sealed record HandSlotEffectRef(string BuffId, IReadOnlyDictionary<string, object>? Params);
```

`Src/mod/combat/HandSlot.cs`：

```csharp
namespace KemoCard.Mod.Combat;

public sealed class HandSlot
{
    private readonly List<HandSlotEffectRef> _slotEffects = [];

    public int SlotIndex { get; }
    public string? CardId { get; private set; }
    public string? RuntimeInstanceId { get; private set; }
    public IReadOnlyList<HandSlotEffectRef> SlotEffects => _slotEffects;
    public bool IsEmpty => CardId is null;

    public HandSlot(int slotIndex) => SlotIndex = slotIndex;

    public void PlaceCard(string cardId, string runtimeInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeInstanceId);
        CardId = cardId;
        RuntimeInstanceId = runtimeInstanceId;
    }

    public void ClearCard()
    {
        CardId = null;
        RuntimeInstanceId = null;
    }

    public bool TryAddSlotEffect(string buffId, IReadOnlyDictionary<string, object>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(buffId))
            return false;

        _slotEffects.Add(new HandSlotEffectRef(buffId, parameters));
        return true;
    }

    public bool TryRemoveSlotEffect(string buffId)
    {
        var index = _slotEffects.FindIndex(effect => effect.BuffId == buffId);
        if (index < 0)
            return false;
        _slotEffects.RemoveAt(index);
        return true;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~HandSlotTests
```

Expected: **PASS**

- [ ] **Step 5: 提交**

```powershell
git add Src/mod/combat/HandSlotEffectRef.cs Src/mod/combat/HandSlot.cs Tests/kemo_card.Ui.Tests/Combat/HandSlotTests.cs
git commit -m "feat(combat): 添加 HandSlot 与槽位效果占位"
```

---

### Task 6：`CharacterBattleInstance`

**Files:**
- Create: `Src/mod/combat/CardRuntimeEntry.cs`
- Create: `Src/mod/combat/CharacterBattleInstance.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CharacterBattleInstanceTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CharacterBattleInstanceTests
{
    [Test]
    public void TryCreate_builds_shuffled_draw_pile_and_energy_from_attributes()
    {
        var registry = CombatTestHelper.CreateRegistry(
            new CardDto
            {
                Id = "strike",
                Stats = new CardStatBlockDto { HpCap = 4, MaxEnergy = 2, InitialEnergy = 1 },
            },
            new CardDto
            {
                Id = "strike_plus",
                Stats = new CardStatBlockDto { HpCap = 5, MaxEnergy = 1, InitialEnergy = 0 },
            });
        var source = new CharacterInstance(new CharacterDto { Id = "kemo", Cards = ["strike", "strike_plus"] });
        var rng = new HostRng(42, "combat.deck");

        var battle = CharacterBattleInstance.TryCreate(source, registry, rng, out var error);

        Assert.That(error, Is.Null);
        Assert.That(battle, Is.Not.Null);
        Assert.That(battle!.DrawPile, Has.Count.EqualTo(2));
        Assert.That(battle.HandSlots, Has.Length.EqualTo(CombatConstants.HandSlotCount));
        Assert.That(battle.HandSlots.All(slot => slot.IsEmpty), Is.True);
        Assert.That(battle.CurrentEnergy, Is.EqualTo(1));
        Assert.That(battle.MaxEnergy, Is.EqualTo(3));
        Assert.That(battle.BaseAttributes.HpCap, Is.EqualTo(9));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~CharacterBattleInstanceTests
```

Expected: **FAIL**

- [ ] **Step 3: 实现**

`Src/mod/combat/CardRuntimeEntry.cs`：

```csharp
namespace KemoCard.Mod.Combat;

public sealed record CardRuntimeEntry(string CardId, string RuntimeInstanceId);
```

`Src/mod/combat/CharacterBattleInstance.cs`：

```csharp
using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;

namespace KemoCard.Mod.Combat;

public sealed class CharacterBattleInstance
{
    private readonly List<CardRuntimeEntry> _drawPile = [];
    private readonly List<CardRuntimeEntry> _graveyard = [];
    private readonly HandSlot[] _handSlots;

    public string SourceInstanceId { get; }
    public string DefinitionId { get; }
    public IReadOnlyList<CardRuntimeEntry> DrawPile => _drawPile;
    public IReadOnlyList<CardRuntimeEntry> Graveyard => _graveyard;
    public IReadOnlyList<HandSlot> HandSlots => _handSlots;
    public CharacterAttributes BaseAttributes { get; }
    public int CurrentEnergy { get; private set; }
    public int MaxEnergy { get; }
    public int EnergyCap { get; }
    public bool HasActed { get; private set; }

    private CharacterBattleInstance(
        string sourceInstanceId,
        string definitionId,
        CharacterAttributes baseAttributes,
        IEnumerable<CardRuntimeEntry> drawPile,
        int currentEnergy,
        int maxEnergy,
        int energyCap)
    {
        SourceInstanceId = sourceInstanceId;
        DefinitionId = definitionId;
        BaseAttributes = baseAttributes;
        _drawPile.AddRange(drawPile);
        CurrentEnergy = currentEnergy;
        MaxEnergy = maxEnergy;
        EnergyCap = energyCap;
        _handSlots = Enumerable.Range(0, CombatConstants.HandSlotCount)
            .Select(index => new HandSlot(index))
            .ToArray();
    }

    public static CharacterBattleInstance? TryCreate(
        CharacterInstance source,
        GameDefinitionRegistry definitions,
        HostRng rng,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(rng);

        var deck = source.GetCurrentDeck();
        if (deck is null)
        {
            error = "当前角色没有可用卡组。";
            return null;
        }

        var validation = deck.Validate(source.GetBuildableCardIds([]));
        if (!validation.IsValid)
        {
            error = "当前卡组构筑非法。";
            return null;
        }

        var baseAttributes = source.ComputeAttributes(definitions);
        var drawPile = deck.CardIds
            .Select(cardId => new CardRuntimeEntry(cardId, Guid.NewGuid().ToString("N")))
            .ToList();

        Shuffle(drawPile, rng);

        error = null;
        return new CharacterBattleInstance(
            source.InstanceId,
            source.DefinitionId,
            baseAttributes,
            drawPile,
            baseAttributes.InitialEnergy,
            baseAttributes.MaxEnergy,
            baseAttributes.MaxEnergy);
    }

    public void SetHasActed(bool hasActed) => HasActed = hasActed;

    public void MoveTopDrawToGraveyard()
    {
        if (_drawPile.Count == 0)
            return;
        var entry = _drawPile[^1];
        _drawPile.RemoveAt(_drawPile.Count - 1);
        _graveyard.Add(entry);
    }

    private static void Shuffle(List<CardRuntimeEntry> entries, HostRng rng)
    {
        for (var i = entries.Count - 1; i > 0; i--)
        {
            var j = rng.NextInt(0, i + 1);
            (entries[i], entries[j]) = (entries[j], entries[i]);
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~CharacterBattleInstanceTests
```

Expected: **PASS**

- [ ] **Step 5: 提交**

```powershell
git add Src/mod/combat/CardRuntimeEntry.cs Src/mod/combat/CharacterBattleInstance.cs Tests/kemo_card.Ui.Tests/Combat/CharacterBattleInstanceTests.cs
git commit -m "feat(combat): 添加 CharacterBattleInstance 进战斗牌区初始化"
```

---

### Task 7：全量 Combat 测试冒烟

**Files:**
- Test: `Tests/kemo_card.Ui.Tests/Combat/`（全部）

- [ ] **Step 1: 运行全部 Combat 测试**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release --filter FullyQualifiedName~KemoCard.Ui.Tests.Combat
```

Expected: **全部 PASS**

- [ ] **Step 2: 运行既有内容测试确保无回归**

```powershell
dotnet test "Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj" -c Release
```

Expected: **全部 PASS**

---

## 规格自检

| 规格要求 | 对应任务 |
|----------|----------|
| 动态卡组 1~10、不可删、新建默认专属卡 | Task 3、4 |
| 构筑校验（已获得 + 专属） | Task 3、4（参数传入 obtainedCardIds） |
| 属性由卡组卡牌 stats 求和 | Task 1、2、4、6 |
| 手牌 5 槽 + 槽位效果占位 | Task 5 |
| 进战斗洗牌牌库、能量初始化 | Task 6 |
| 不含 BuffInstance / 局内存档 / 战斗流程 | 未列入任何 Task |

---

## 后续里程碑（单独文档）

- `BuffInstance` + 槽位/角色 Buff 结算
- `ObtainedCardPool` + `RunSaveDto` + `CharacterInstance` 序列化
- `TeamBattleState` 队伍共用 HP
- `CombatTurnController` 战斗阶段机
