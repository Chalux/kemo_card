# GAS 风格 Buff 系统与属性集实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，按任务逐步实现。步骤使用 checkbox（`- [ ]`）追踪。

**Goal:** 参考 UE GAS 实现数据驱动属性集 + GameplayEffect（替代 BuffDto）+ 全量 GameplayTags + 三类 ASC，并重构 content 管线与战斗结算接入；取代固定 `CharacterAttributes`，支持修饰器聚合、Execution 伤害、队伍派生 MaxHealth 与 delta_follow 同步。

**Architecture:** 可复用 GAS 核心放在 `Src/frame/gas/`（AttributeSet、Aggregator、Magnitude、ASC、ActiveGE、TagContainer、Execution）；内容 DTO 在 `Src/frame/content/definitions/`；战斗接线在 `Src/mod/combat/`（三类 ASC 归属、TeamMaxHealthCoordinator、CombatEffectExecutor 分流 GE vs 技能动作）。属性类效果统一走 `ApplyGameplayEffect`；Draw/Discard/ExecuteScript 等保留为 `SkillActionDto`。

**Tech Stack:** Godot 4.6.1 Mono + .NET 8 + C# 12 + NUnit 4 + 现有 `GameDefinitionRegistry` / `CombatSimulation` / `CombatRuleEngine`

**设计来源:** [GAS 风格 Buff 与属性集计划](../../.cursor/plans/gas风格buff与属性集_69c69402.plan.md)

---

## 文件结构

| 路径 | 职责 |
|------|------|
| `Src/frame/gas/AttributeIds.cs` | 内置属性 id 常量（Health/MaxHealth/PhysicalAttack/Damage 等） |
| `Src/frame/gas/AttributeValue.cs` | Base + Current |
| `Src/frame/gas/AttributeSet.cs` | 动态属性字典 + 变更事件 |
| `Src/frame/gas/EAttributeModifierOp.cs` | Add/Multiply/Divide/Override |
| `Src/frame/gas/AttributeModifier.cs` | 运行时修饰器实例 |
| `Src/frame/gas/AttributeAggregator.cs` | 聚合顺序求 Current |
| `Src/frame/gas/Magnitude/` | Scalar/SetByCaller/AttributeBased/Custom 幅值求值 |
| `Src/frame/gas/GameplayTag.cs` | 层级标签匹配 |
| `Src/frame/gas/GameplayTagContainer.cs` | 标签集合 + Has/Match/Add/Remove |
| `Src/frame/gas/AbilitySystemComponent.cs` | 属性集 + ActiveGE + 标签 + Apply/Remove |
| `Src/frame/gas/ActiveGameplayEffect.cs` | 运行时 GE 实例（堆叠/回合/周期） |
| `Src/frame/gas/GameplayEffectSpec.cs` | 一次施加的 spec（def + setByCaller + source） |
| `Src/frame/gas/Executions/` | IExecutionCalculation、DamageExecution |
| `Src/frame/content/definitions/AttributeDefDto.cs` | 属性定义 JSON DTO |
| `Src/frame/content/definitions/GameplayEffectDefDto.cs` | 替代 BuffDto |
| `Src/frame/content/definitions/SkillActionDto.cs` | 精简编排动作（原 EffectDto 非属性部分） |
| `Src/frame/content/definitions/GasDefinitionDtos.cs` | ModifierDef、MagnitudeDef、ExecutionDef、Hook DTO |
| `Src/mod/combat/gas/CombatAscFactory.cs` | 三类 ASC 初始化 |
| `Src/mod/combat/gas/TeamMaxHealthCoordinator.cs` | 派生 MaxHealth + delta_follow |
| `Src/mod/combat/gas/CombatGasBridge.cs` | CombatTargetRef → ASC 解析 |
| `Tests/kemo_card.Ui.Tests/Gas/GasTestHelper.cs` | 属性/GE 测试辅助 |
| `Tests/kemo_card.Ui.Tests/Gas/*Tests.cs` | GAS 单元测试 |
| `Config/mods/base-game/content/attributes/*.json` | 内置属性定义 |
| `Config/mods/base-game/content/gameplay_effects/*.json` | 迁移自 buffs + 属性类 effects |

---

## 阶段 1：属性系统核心

### Task 0：内置属性 id 与 AttributeDefDto

**Files:**
- Create: `Src/frame/gas/AttributeIds.cs`
- Create: `Src/frame/content/definitions/AttributeDefDto.cs`
- Create: `Config/mods/base-game/content/attributes/health.json`（示例）
- Test: `Tests/kemo_card.Ui.Tests/Gas/AttributeDefTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Text.Json;
using KemoCard.Frame.Content.Definitions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class AttributeDefTests
{
    [Test]
    public void AttributeDef_deserializes_from_json()
    {
        const string json = """
            {
              "displayNameId": "attr.health.name",
              "defaultBase": 0,
              "allowNegative": false,
              "isMeta": false
            }
            """;
        var dto = JsonSerializer.Deserialize<AttributeDefDto>(json, ContentDefinitionJson.Options);
        Assert.That(dto!.DefaultBase, Is.EqualTo(0));
        Assert.That(dto.IsMeta, Is.False);
    }

    [Test]
    public void AttributeIds_has_well_known_constants()
    {
        Assert.That(AttributeIds.Health, Is.EqualTo("Health"));
        Assert.That(AttributeIds.MaxHealth, Is.EqualTo("MaxHealth"));
        Assert.That(AttributeIds.Damage, Is.EqualTo("Damage"));
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter FullyQualifiedName~AttributeDefTests -v n`

Expected: FAIL — type not found

- [ ] **Step 3: 实现**

`AttributeIds.cs`:

```csharp
namespace KemoCard.Frame.Gas;

public static class AttributeIds
{
    public const string Health = "Health";
    public const string MaxHealth = "MaxHealth";
    public const string PhysicalAttack = "PhysicalAttack";
    public const string PhysicalDefense = "PhysicalDefense";
    public const string MagicAttack = "MagicAttack";
    public const string MagicDefense = "MagicDefense";
    public const string HealPower = "HealPower";
    public const string MaxEnergy = "MaxEnergy";
    public const string InitialEnergy = "InitialEnergy";
    public const string Damage = "Damage";
    public const string Healing = "Healing";
}
```

`AttributeDefDto.cs`:

```csharp
using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class AttributeDefDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayNameId")]
    public string DisplayNameId { get; init; } = "";

    [JsonPropertyName("defaultBase")]
    public float DefaultBase { get; init; }

    [JsonPropertyName("allowNegative")]
    public bool AllowNegative { get; init; }

    [JsonPropertyName("minValue")]
    public float? MinValue { get; init; }

    [JsonPropertyName("maxValue")]
    public float? MaxValue { get; init; }

    [JsonPropertyName("isMeta")]
    public bool IsMeta { get; init; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];
}
```

`attributes/health.json`:

```json
{
  "displayNameId": "attr.health.name",
  "defaultBase": 0,
  "allowNegative": false,
  "isMeta": false
}
```

- [ ] **Step 4: 运行测试通过**

Expected: PASS

- [ ] **Step 5: 提交**

```powershell
git add Src/frame/gas/AttributeIds.cs Src/frame/content/definitions/AttributeDefDto.cs Config/mods/base-game/content/attributes/ Tests/kemo_card.Ui.Tests/Gas/AttributeDefTests.cs
git commit -m "feat(gas): 添加属性 id 常量与 AttributeDefDto"
```

---

### Task 1：AttributeValue 与 AttributeSet

**Files:**
- Create: `Src/frame/gas/AttributeValue.cs`
- Create: `Src/frame/gas/AttributeSet.cs`
- Create: `Tests/kemo_card.Ui.Tests/Gas/GasTestHelper.cs`
- Test: `Tests/kemo_card.Ui.Tests/Gas/AttributeSetTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class AttributeSetTests
{
    [Test]
    public void SetBaseValue_updates_current_when_no_modifiers()
    {
        var set = GasTestHelper.CreateAttributeSet((AttributeIds.MaxHealth, 10f));
        set.SetBaseValue(AttributeIds.MaxHealth, 25f);
        Assert.That(set.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(25f));
    }

    [Test]
    public void OnAttributeChanged_fires_when_base_changes()
    {
        var set = GasTestHelper.CreateAttributeSet((AttributeIds.MaxHealth, 10f));
        string? changed = null;
        set.AttributeChanged += (_, e) => changed = e.AttributeId;
        set.SetBaseValue(AttributeIds.MaxHealth, 15f);
        Assert.That(changed, Is.EqualTo(AttributeIds.MaxHealth));
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test ... --filter FullyQualifiedName~AttributeSetTests -v n`

Expected: FAIL

- [ ] **Step 3: 实现**

`AttributeValue.cs`:

```csharp
namespace KemoCard.Frame.Gas;

public sealed class AttributeValue
{
    public float BaseValue { get; private set; }
    public float CurrentValue { get; internal set; }

    public AttributeValue(float baseValue)
    {
        BaseValue = baseValue;
        CurrentValue = baseValue;
    }

    public void SetBase(float value) => BaseValue = value;
}
```

`AttributeSet.cs`:

```csharp
namespace KemoCard.Frame.Gas;

public sealed class AttributeSet
{
    private readonly Dictionary<string, AttributeValue> _values = new(StringComparer.Ordinal);

    public event EventHandler<AttributeChangedEventArgs>? AttributeChanged;

    public void InitAttribute(string attributeId, float baseValue)
    {
        _values[attributeId] = new AttributeValue(baseValue);
    }

    public bool HasAttribute(string attributeId) => _values.ContainsKey(attributeId);

    public float GetBaseValue(string attributeId) =>
        _values.TryGetValue(attributeId, out var v) ? v.BaseValue : 0f;

    public float GetCurrentValue(string attributeId) =>
        _values.TryGetValue(attributeId, out var v) ? v.CurrentValue : 0f;

    public void SetBaseValue(string attributeId, float baseValue)
    {
        if (!_values.TryGetValue(attributeId, out var value))
        {
            InitAttribute(attributeId, baseValue);
            RaiseChanged(attributeId);
            return;
        }
        value.SetBase(baseValue);
        value.CurrentValue = baseValue;
        RaiseChanged(attributeId);
    }

    internal AttributeValue GetOrCreate(string attributeId, float defaultBase = 0f)
    {
        if (!_values.TryGetValue(attributeId, out var value))
        {
            value = new AttributeValue(defaultBase);
            _values[attributeId] = value;
        }
        return value;
    }

    internal void SetCurrentValue(string attributeId, float current)
    {
        var value = GetOrCreate(attributeId);
        value.CurrentValue = current;
        RaiseChanged(attributeId);
    }

    private void RaiseChanged(string attributeId) =>
        AttributeChanged?.Invoke(this, new AttributeChangedEventArgs(attributeId));
}

public sealed class AttributeChangedEventArgs : EventArgs
{
    public string AttributeId { get; }
    public AttributeChangedEventArgs(string attributeId) => AttributeId = attributeId;
}
```

`GasTestHelper.cs`:

```csharp
using KemoCard.Frame.Gas;

namespace KemoCard.Ui.Tests.Gas;

internal static class GasTestHelper
{
    public static AttributeSet CreateAttributeSet(params (string Id, float Base)[] attrs)
    {
        var set = new AttributeSet();
        foreach (var (id, baseValue) in attrs)
            set.InitAttribute(id, baseValue);
        return set;
    }
}
```

- [ ] **Step 4–5: 测试通过并提交**

```powershell
git commit -m "feat(gas): 添加 AttributeSet 与属性变更事件"
```

---

### Task 2：AttributeAggregator（Add/Mul/Div/Override）

**Files:**
- Create: `Src/frame/gas/EAttributeModifierOp.cs`
- Create: `Src/frame/gas/AttributeModifier.cs`
- Create: `Src/frame/gas/AttributeAggregator.cs`
- Test: `Tests/kemo_card.Ui.Tests/Gas/AttributeAggregatorTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Test]
public void Aggregator_applies_add_then_multiply_then_override()
{
    var set = GasTestHelper.CreateAttributeSet((AttributeIds.PhysicalAttack, 10f));
    var agg = new AttributeAggregator(set);
    agg.SetModifiers(AttributeIds.PhysicalAttack,
    [
        new AttributeModifier(EAttributeModifierOp.Add, 5f, order: 0),
        new AttributeModifier(EAttributeModifierOp.Multiply, 0.5f, order: 1),
        new AttributeModifier(EAttributeModifierOp.Override, 99f, order: 2),
    ]);
    agg.Recalculate(AttributeIds.PhysicalAttack);
    Assert.That(set.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(99f));
}

[Test]
public void Aggregator_formula_without_override()
{
    var set = GasTestHelper.CreateAttributeSet((AttributeIds.PhysicalAttack, 10f));
    var agg = new AttributeAggregator(set);
    agg.SetModifiers(AttributeIds.PhysicalAttack,
    [
        new AttributeModifier(EAttributeModifierOp.Add, 5f, order: 0),
        new AttributeModifier(EAttributeModifierOp.Multiply, 0.5f, order: 1),
    ]);
    agg.Recalculate(AttributeIds.PhysicalAttack);
    Assert.That(set.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(7.5f));
}
```

- [ ] **Step 2: 运行确认失败**

Expected: FAIL

- [ ] **Step 3: 实现**

`EAttributeModifierOp.cs`: `Add`, `Multiply`, `Divide`, `Override`

`AttributeModifier.cs`: `Op`, `Magnitude`, `Order`, `SourceHandle`（可选，用于按来源移除）

`AttributeAggregator.cs`:

```csharp
public sealed class AttributeAggregator
{
    private readonly AttributeSet _set;
    private readonly Dictionary<string, List<AttributeModifier>> _modifiers = new(StringComparer.Ordinal);

    public void SetModifiers(string attributeId, IReadOnlyList<AttributeModifier> modifiers)
    {
        _modifiers[attributeId] = modifiers.OrderBy(m => m.Order).ToList();
    }

    public void Recalculate(string attributeId)
    {
        var baseValue = _set.GetBaseValue(attributeId);
        if (!_modifiers.TryGetValue(attributeId, out var list) || list.Count == 0)
        {
            _set.SetCurrentValue(attributeId, baseValue);
            return;
        }

        if (list.Any(m => m.Op == EAttributeModifierOp.Override))
        {
            var lastOverride = list.Last(m => m.Op == EAttributeModifierOp.Override);
            _set.SetCurrentValue(attributeId, lastOverride.Magnitude);
            return;
        }

        var add = list.Where(m => m.Op == EAttributeModifierOp.Add).Sum(m => m.Magnitude);
        var mul = list.Where(m => m.Op == EAttributeModifierOp.Multiply)
            .Aggregate(1f, (acc, m) => acc * (1f + m.Magnitude));
        var div = list.Where(m => m.Op == EAttributeModifierOp.Divide)
            .Aggregate(1f, (acc, m) => acc * m.Magnitude);
        var current = div == 0f ? baseValue : ((baseValue + add) * mul) / div;
        _set.SetCurrentValue(attributeId, current);
    }

    public void RecalculateAll()
    {
        foreach (var attributeId in _modifiers.Keys)
            Recalculate(attributeId);
    }
}
```

- [ ] **Step 4–5: 测试通过并提交**

```powershell
git commit -m "feat(gas): 添加属性修饰器聚合器"
```

---

### Task 3：Magnitude 多来源求值

**Files:**
- Create: `Src/frame/gas/Magnitude/IMagnitudeEvaluator.cs`
- Create: `Src/frame/gas/Magnitude/MagnitudeEvaluationContext.cs`
- Create: `Src/frame/gas/Magnitude/MagnitudeEvaluator.cs`
- Create: `Src/frame/content/definitions/GasDefinitionDtos.cs`（`MagnitudeDefDto`）
- Test: `Tests/kemo_card.Ui.Tests/Gas/MagnitudeEvaluatorTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Test]
public void Scalar_magnitude_returns_constant()
{
    var ctx = GasTestHelper.CreateMagnitudeContext();
    var eval = new MagnitudeEvaluator();
    var def = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = 6f };
    Assert.That(eval.Evaluate(def, ctx), Is.EqualTo(6f));
}

[Test]
public void SetByCaller_reads_named_value_from_spec()
{
    var ctx = GasTestHelper.CreateMagnitudeContext(setByCaller: new Dictionary<string, float> { ["Amount"] = 8f });
    var eval = new MagnitudeEvaluator();
    var def = new MagnitudeDefDto { Kind = EMagnitudeKind.SetByCaller, CallerName = "Amount" };
    Assert.That(eval.Evaluate(def, ctx), Is.EqualTo(8f));
}

[Test]
public void AttributeBased_multiplies_source_attribute()
{
    var source = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
    var ctx = GasTestHelper.CreateMagnitudeContext(sourceAsc: source);
    var eval = new MagnitudeEvaluator();
    var def = new MagnitudeDefDto
    {
        Kind = EMagnitudeKind.AttributeBased,
        AttributeId = AttributeIds.PhysicalAttack,
        Coefficient = 1.5f,
        Capture = EAttributeCapture.Source,
    };
    Assert.That(eval.Evaluate(def, ctx), Is.EqualTo(15f));
}
```

- [ ] **Step 2–4: 实现 `EMagnitudeKind`（Scalar/SetByCaller/AttributeBased/Custom）与 `MagnitudeEvaluator`**

`GasDefinitionDtos.cs` 片段：

```csharp
public enum EMagnitudeKind { Scalar, SetByCaller, AttributeBased, Custom }

public sealed class MagnitudeDefDto
{
    [JsonPropertyName("kind")]
    public EMagnitudeKind Kind { get; init; }

    [JsonPropertyName("scalar")]
    public float Scalar { get; init; }

    [JsonPropertyName("callerName")]
    public string? CallerName { get; init; }

    [JsonPropertyName("attributeId")]
    public string? AttributeId { get; init; }

    [JsonPropertyName("coefficient")]
    public float Coefficient { get; init; } = 1f;

    [JsonPropertyName("capture")]
    public EAttributeCapture Capture { get; init; }
}

public enum EAttributeCapture { Source, Target }
```

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(gas): 添加 Magnitude 多来源幅值求值"
```

---

### Task 4：AbilitySystemComponent 骨架

**Files:**
- Create: `Src/frame/gas/AbilitySystemComponent.cs`
- Test: `Tests/kemo_card.Ui.Tests/Gas/AbilitySystemComponentTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Test]
public void Asc_exposes_attribute_set_and_reads_current()
{
    var asc = new AbilitySystemComponent();
    asc.Attributes.InitAttribute(AttributeIds.MaxHealth, 20f);
    Assert.That(asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(20f));
}
```

- [ ] **Step 2–4: 实现 ASC 骨架**

```csharp
public sealed class AbilitySystemComponent
{
    public AttributeSet Attributes { get; } = new();
    public AttributeAggregator Aggregator { get; }
    public GameplayTagContainer Tags { get; } = new();

    private readonly List<ActiveGameplayEffect> _activeEffects = [];

    public AbilitySystemComponent()
    {
        Aggregator = new AttributeAggregator(Attributes);
    }

    public float GetCurrentValue(string attributeId) => Attributes.GetCurrentValue(attributeId);

    public float GetBaseValue(string attributeId) => Attributes.GetBaseValue(attributeId);

    public IReadOnlyList<ActiveGameplayEffect> ActiveEffects => _activeEffects;
}
```

（`ActiveGameplayEffect` 在 Task 6 补全；本 Task 仅骨架 + 属性读写。）

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(gas): 添加 AbilitySystemComponent 骨架"
```

---

## 阶段 2：GameplayEffect 运行时

### Task 5：GameplayEffectDef DTO

**Files:**
- Create: `Src/frame/content/definitions/GameplayEffectDefDto.cs`
- Extend: `Src/frame/content/definitions/GasDefinitionDtos.cs`
- Create: `Config/mods/base-game/content/gameplay_effects/weak.json`（迁移示例）
- Test: `Tests/kemo_card.Ui.Tests/Gas/GameplayEffectDefTests.cs`

- [ ] **Step 1: 写失败测试 — 反序列化 weak 等价 GE**

```csharp
const string json = """
{
  "displayNameId": "ge.weak.name",
  "durationPolicy": "HasDuration",
  "durationTurns": 2,
  "stackingPolicy": "AggregateByTarget",
  "maxStacks": 3,
  "modifiers": [
    { "attributeId": "DamageTakenScale", "operation": "Add", "magnitude": { "kind": "Scalar", "scalar": 1 } }
  ],
  "grantedTags": ["debuff.weak"],
  "hooks": { "onApply": [{ "actionId": "weak_apply_marker" }] }
}
""";
```

- [ ] **Step 2–4: 实现 DTO**

关键枚举（`ContentEnums.cs` 或 `GasDefinitionDtos.cs`）：

```csharp
public enum EDurationPolicy { Instant, HasDuration, Infinite }
public enum EStackingPolicy { None, AggregateBySource, AggregateByTarget }
public enum EAttributeModifierOp { Add, Multiply, Divide, Override }

public sealed class AttributeModifierDefDto
{
    [JsonPropertyName("attributeId")]
    public string AttributeId { get; init; } = "";

    [JsonPropertyName("operation")]
    public EAttributeModifierOp Operation { get; init; }

    [JsonPropertyName("magnitude")]
    public MagnitudeDefDto Magnitude { get; init; } = new();
}

public sealed class GameplayEffectHooksDto
{
    [JsonPropertyName("onApply")]
    public List<SkillActionRefDto> OnApply { get; init; } = [];
    // onTurnStart/onTurnEnd/onStackChanged/onRemove 同理
}

public sealed class GameplayEffectDefDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("durationPolicy")]
    public EDurationPolicy DurationPolicy { get; init; }

    [JsonPropertyName("durationTurns")]
    public int DurationTurns { get; init; }

    [JsonPropertyName("periodTurns")]
    public int PeriodTurns { get; init; }

    [JsonPropertyName("stackingPolicy")]
    public EStackingPolicy StackingPolicy { get; init; }

    [JsonPropertyName("maxStacks")]
    public int MaxStacks { get; init; } = 1;

    [JsonPropertyName("modifiers")]
    public List<AttributeModifierDefDto> Modifiers { get; init; } = [];

    [JsonPropertyName("executions")]
    public List<ExecutionDefDto> Executions { get; init; } = [];

    [JsonPropertyName("grantedTags")]
    public List<string> GrantedTags { get; init; } = [];

    [JsonPropertyName("applicationRequiredTags")]
    public List<string> ApplicationRequiredTags { get; init; } = [];

    [JsonPropertyName("applicationBlockedTags")]
    public List<string> ApplicationBlockedTags { get; init; } = [];

    [JsonPropertyName("ongoingRequiredTags")]
    public List<string> OngoingRequiredTags { get; init; } = [];

    [JsonPropertyName("immunityTags")]
    public List<string> ImmunityTags { get; init; } = [];

    [JsonPropertyName("removeEffectsWithTags")]
    public List<string> RemoveEffectsWithTags { get; init; } = [];

    [JsonPropertyName("hooks")]
    public GameplayEffectHooksDto Hooks { get; init; } = new();
}
```

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(gas): 添加 GameplayEffectDefDto"
```

---

### Task 6：ActiveGameplayEffect 与 GameplayEffectSpec

**Files:**
- Create: `Src/frame/gas/GameplayEffectSpec.cs`
- Create: `Src/frame/gas/ActiveGameplayEffect.cs`
- Test: `Tests/kemo_card.Ui.Tests/Gas/ActiveGameplayEffectTests.cs`

- [ ] **Step 1: 写失败测试 — 堆叠与回合衰减**

```csharp
[Test]
public void HasDuration_decrements_on_turn_end()
{
    var active = new ActiveGameplayEffect(/* def duration=2 */, spec, handle: 1);
    active.OnTurnEnd();
    Assert.That(active.RemainingTurns, Is.EqualTo(1));
    active.OnTurnEnd();
    Assert.That(active.IsExpired, Is.True);
}

[Test]
public void AggregateByTarget_increments_stacks_up_to_max()
{
    // 两次 Apply 同 def → Stacks == 2
}
```

- [ ] **Step 2–4: 实现 `GameplayEffectSpec`（Def + SourceAsc + TargetAsc + SetByCaller）与 `ActiveGameplayEffect`（RemainingTurns、Stacks、Handle、IsSuspended）**

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(gas): 添加 ActiveGameplayEffect 与 Spec"
```

---

### Task 7：ASC.ApplyGameplayEffect / Remove

**Files:**
- Modify: `Src/frame/gas/AbilitySystemComponent.cs`
- Create: `Src/frame/gas/GameplayEffectApplicationContext.cs`
- Test: `Tests/kemo_card.Ui.Tests/Gas/ApplyGameplayEffectTests.cs`

- [ ] **Step 1: 写失败测试 — Instant GE 修改 BaseValue**

```csharp
[Test]
public void Instant_ge_adds_to_base_max_health()
{
    var asc = new AbilitySystemComponent();
    asc.Attributes.InitAttribute(AttributeIds.MaxHealth, 10f);
    var def = GasTestHelper.InstantAddModifier(AttributeIds.MaxHealth, 5f);
    asc.ApplyGameplayEffect(new GameplayEffectSpec(def));
    Assert.That(asc.GetBaseValue(AttributeIds.MaxHealth), Is.EqualTo(15f));
}
```

- [ ] **Step 2–4: 实现 Apply 流程**

1. 检查 `applicationRequiredTags` / `applicationBlockedTags` / `immunityTags`（Task 10 补全标签；本 Task 可先 stub 为始终通过）
2. `removeEffectsWithTags` 移除冲突 GE
3. `Instant`：直接改 Base 或走 Execution（Task 12）；不加入 `_activeEffects`
4. `HasDuration/Infinite`：创建 `ActiveGameplayEffect`，注册修饰器到 Aggregator，`RecalculateAll`
5. 授予 `grantedTags`（Task 10）
6. 触发 hooks（通过回调注入，战斗层实现）

`RemoveActiveEffectsByTag` / `RemoveActiveEffect(handle)`

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(gas): 实现 ASC.ApplyGameplayEffect 与移除"
```

---

### Task 8：回合推进（Duration / Periodic / Hooks）

**Files:**
- Modify: `Src/frame/gas/AbilitySystemComponent.cs`
- Test: `Tests/kemo_card.Ui.Tests/Gas/GameplayEffectTurnTests.cs`

- [ ] **Step 1: 写失败测试 — onTurnStart 周期触发**

```csharp
[Test]
public void Periodic_ge_fires_every_n_turns()
{
    // periodTurns=2 的 GE，TurnStart x3 → 触发 2 次（第 1、3 回合）
}
```

- [ ] **Step 2–4: 实现 `OnTurnStart` / `OnTurnEnd`**

- `OnTurnEnd`：`RemainingTurns--`；过期则 Remove 并触发 onRemove
- `OnTurnStart`：周期计数；触发 onTurnStart hook；检查 `ongoingRequiredTags` 挂起/恢复
- 挂起的 GE 不参与 Aggregator

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(gas): 实现 GE 回合衰减与周期触发"
```

---

## 阶段 3：GameplayTags 全量

### Task 9：GameplayTag 层级匹配

**Files:**
- Create: `Src/frame/gas/GameplayTag.cs`
- Create: `Src/frame/gas/GameplayTagContainer.cs`
- Create: `Src/frame/content/definitions/GameplayTagDefDto.cs`
- Test: `Tests/kemo_card.Ui.Tests/Gas/GameplayTagTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Test]
public void HasTag_matches_parent_tag()
{
    var container = new GameplayTagContainer();
    container.AddTag("debuff.weak");
    Assert.That(container.HasTag("debuff"), Is.True);
    Assert.That(container.HasTag("debuff.weak"), Is.True);
    Assert.That(container.HasTag("buff"), Is.False);
}

[Test]
public void HasAny_and_HasAll_work()
{
    var c = new GameplayTagContainer();
    c.AddTag("debuff.weak");
    Assert.That(c.HasAll(["debuff"]), Is.True);
    Assert.That(c.HasAny(["buff", "debuff"]), Is.True);
}
```

- [ ] **Step 2–4: 实现点分层级：`HasTag(query)` 当 container 含 `query` 或 container 中任 tag 以 `query.` 为前缀**

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(gas): 添加 GameplayTag 层级容器"
```

---

### Task 10：GE 标签条件、免疫、驱散

**Files:**
- Modify: `Src/frame/gas/AbilitySystemComponent.cs`
- Test: `Tests/kemo_card.Ui.Tests/Gas/GameplayEffectTagTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Test]
public void Application_blocked_when_target_has_blocked_tag()
{
    var target = new AbilitySystemComponent();
    target.Tags.AddTag("state.invulnerable");
    var def = new GameplayEffectDefDto
    {
        ApplicationBlockedTags = ["state.invulnerable"],
        DurationPolicy = EDurationPolicy.Instant,
    };
    var result = target.ApplyGameplayEffect(new GameplayEffectSpec(def));
    Assert.That(result.Success, Is.False);
}

[Test]
public void Immunity_prevents_application()
{
    var target = new AbilitySystemComponent();
    target.Tags.AddTag("immunity.debuff");
    var def = new GameplayEffectDefDto { ImmunityTags = ["debuff"] /* ... */ };
    // Apply 失败
}

[Test]
public void RemoveEffectsWithTags_dispels_matching_active_ge()
{
    // 目标已有 grantedTags debuff.poison 的 GE；新 GE removeEffectsWithTags=["debuff"] → 旧 GE 被移除
}
```

- [ ] **Step 2–4: 在 Apply 流程接入；`RefreshGrantedTags()` 从所有未挂起 ActiveGE 重建 Tags（固有标签保留）**

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(gas): 实现 GE 标签条件免疫与驱散"
```

---

### Task 11：标签目录与 content 校验

**Files:**
- Modify: `Src/frame/content/ContentCategory.cs`（增 `Attribute`、`GameplayEffect`、`GameplayTag`）
- Modify: `Src/frame/content/ContentModLoader.cs`
- Modify: `Src/frame/content/GameDefinitionStore.cs`
- Modify: `Src/frame/content/ContentDefinitionValidator.cs`
- Create: `Config/mods/base-game/content/tags/*.json`（可选注册 debuff/buff/state 等）
- Test: `Tests/kemo_card.Ui.Tests/Gas/GameplayTagRegistryTests.cs`

- [ ] **Step 1–4: 加载 `tags/`、`attributes/`、`gameplay_effects/`；校验 GE 引用的 attributeId/tag 存在于 Store**

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(content): 添加 Attribute/GameplayEffect/GameplayTag 内容管线"
```

---

## 阶段 4：伤害 Execution 与战斗接入

### Task 12：DamageExecution

**Files:**
- Create: `Src/frame/gas/Executions/IExecutionCalculation.cs`
- Create: `Src/frame/gas/Executions/DamageExecution.cs`
- Create: `Src/frame/gas/Executions/ExecutionRunner.cs`
- Test: `Tests/kemo_card.Ui.Tests/Gas/DamageExecutionTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Test]
public void DamageExecution_reads_attack_and_defense()
{
    var source = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
    var target = GasTestHelper.CreateAscWithAttributes(
        (AttributeIds.PhysicalDefense, 3f),
        (AttributeIds.Health, 20f));
    var spec = new GameplayEffectSpec(
        GasTestHelper.DamageEffect(setByCallerAmount: 6f),
        sourceAsc: source,
        targetAsc: target);
    DamageExecution.Execute(spec, target);
    // meta Damage = 6 + 10 - 3 = 13 → Health 20-13=7
    Assert.That(target.GetCurrentValue(AttributeIds.Health), Is.EqualTo(7f));
}
```

- [ ] **Step 2–4: 实现 Execution 流程**

1. 写入 meta `Damage`（SetByCaller Amount + AttributeBased 源攻击）
2. 减去目标 `PhysicalDefense`（可配置 execution def）
3. 扣减目标 `Health` Current（Instant 改 Base 或 Meta 管道后 Apply 到 Health — 统一：**Damage meta 经规则管线后改 Health Current**）

`ExecutionDefDto`:

```csharp
public sealed class ExecutionDefDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "Damage";

    [JsonPropertyName("damageType")]
    public string DamageType { get; init; } = "Physical";
}
```

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(gas): 添加 DamageExecution 伤害计算"
```

---

### Task 13：三类 ASC 与 TeamMaxHealthCoordinator

**Files:**
- Create: `Src/mod/combat/gas/CombatAscFactory.cs`
- Create: `Src/mod/combat/gas/TeamMaxHealthCoordinator.cs`
- Modify: `Src/mod/combat/CharacterBattleInstance.cs`
- Modify: `Src/mod/combat/runtime/EnemyUnit.cs`
- Modify: `Src/mod/combat/runtime/PlayerTeamState.cs`
- Test: `Tests/kemo_card.Ui.Tests/Gas/TeamMaxHealthCoordinatorTests.cs`

- [ ] **Step 1: 写失败测试 — delta_follow**

```csharp
[Test]
public void Character_max_health_increase_syncs_team_health_and_max()
{
    var team = /* 2 chars MaxHealth 10 each, team Health=20 MaxHealth=20 */;
    team.Characters[0].Asc.ApplyGameplayEffect(instantAddMaxHealth(5f));
    coordinator.Sync(team);
    Assert.That(team.Asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(25f));
    Assert.That(team.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(25f)); // +5 同步回血
}

[Test]
public void Character_max_health_decrease_clamps_team_health()
{
    // team Health=20 Max=20 → 角色 -5 MaxHealth → team Max=15 Health clamp 15（不额外扣血）
}
```

- [ ] **Step 2–4: 实现**

- `CharacterBattleInstance` 增加 `AbilitySystemComponent Asc`（含 MaxHealth/攻防/能量，**无 Health**）
- `PlayerTeamState` 增加 `AbilitySystemComponent Asc`（Health + 派生 MaxHealth）；移除 int `SharedHp/MaxHp` 或保留为转发属性
- `EnemyUnit` 增加 `Asc`（Health + MaxHealth + 攻防）
- `TeamMaxHealthCoordinator`：订阅各角色 `MaxHealth` 变更 → 重算 `team.MaxHealth = Σ char.MaxHealth.Current` → delta_follow 调整 `team.Health`

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(combat): 接入三类 ASC 与队伍 MaxHealth 派生同步"
```

---

### Task 14：CombatRuleEngine 伤害管线接入 GAS

**Files:**
- Modify: `Src/mod/combat/effects/CombatEffectExecutor.cs`
- Modify: `Src/mod/combat/rules/DamagePacket.cs`
- Modify: `Src/mod/combat/rules/builtin/SharedHpDefeatRule.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CombatGasDamageTests.cs`

- [ ] **Step 1: 写失败测试 — GE 伤害经规则改写**

```csharp
[Test]
public void Damage_ge_respects_combat_rule_engine()
{
    // 规则：伤害 +2；源攻 10 目标防 3 base amount 6 → 最终 Health 扣 15
}
```

- [ ] **Step 2–4: `ApplyDamage` 改为读 ASC Health；`DamagePacket.Amount` 来自 GAS meta；玩家目标路由到 `PlayerTeamState.Asc`**

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(combat): GAS 伤害接入 CombatRuleEngine 管线"
```

---

### Task 15：TeamDomainManager 重做（Infinite GE）

**Files:**
- Modify: `Src/mod/combat/runtime/TeamDomainManager.cs`
- Remove/Deprecate: `CombatDomain.cs`（可选保留为 GE handle 快照）
- Test: `Tests/kemo_card.Ui.Tests/Combat/TeamDomainManagerTests.cs`（更新）

- [ ] **Step 1: 写失败测试 — 同队顶替**

```csharp
[Test]
public void SetDomain_replaces_infinite_ge_on_team_asc()
{
    var sim = CombatSimulationTestBuilder.Minimal(/*...*/);
    sim.DomainManager.TrySetPlayerDomain("domain_a");
    sim.DomainManager.TrySetPlayerDomain("domain_b");
    Assert.That(sim.PlayerTeam.Asc.ActiveEffects.Count(e => e.Def.Id.StartsWith("domain")), Is.EqualTo(1));
}
```

- [ ] **Step 2–4: 领域 = 队伍 ASC 上单个 Infinite GE；顶替时 Remove 旧 handle + Apply 新 def；hooks 仍通过 SkillAction 执行**

- [ ] **Step 5: 提交**

```powershell
git commit -m "refactor(combat): 领域改为队伍 ASC 单例 Infinite GE"
```

---

## 阶段 5：Content 管线重构与迁移

### Task 16：SkillActionDto 替代编排类 EffectDto

**Files:**
- Create: `Src/frame/content/definitions/SkillActionDto.cs`
- Create: `Src/frame/content/definitions/SkillActionRefDto.cs`
- Modify: `Src/frame/content/definitions/ContentEnums.cs`（`ESkillActionKind` 替代 Effect 中 Draw/Discard/...）
- Modify: `SkillDto.cs`、`GameplayEffectHooksDto` 引用 `SkillActionRefDto`
- Test: `Tests/kemo_card.Ui.Tests/Gas/SkillActionDefTests.cs`

- [ ] **Step 1–4: 实现 `SkillActionDto`（kind + params + scriptPath + actionRefs 链式）**

```csharp
public enum ESkillActionKind
{
    Draw,
    Discard,
    GainResource,
    ExecuteScript,
    ChainActions,
    ApplyGameplayEffect,
    RemoveGameplayEffect,
}

public sealed class SkillActionRefDto
{
    [JsonPropertyName("actionId")]
    public string ActionId { get; init; } = "";

    [JsonPropertyName("params")]
    public Dictionary<string, object>? Params { get; init; }
}
```

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(content): 添加 SkillActionDto 编排动作定义"
```

---

### Task 17：Store/Loader/Validator 全面切换

**Files:**
- Modify: `ModDefinitionsBundle.cs`、`ModContentBundle`、`ContentModLoader.cs`、`GameDefinitionStore.cs`、`GameDefinitionRegistry.cs`、`ContentRegistryMerger.cs`、`ModScriptPathCollector.cs`、`ContentDefinitionValidator.cs`
- Test: `Tests/kemo_card.Ui.Tests/Gas/ContentPipelineGasTests.cs`

- [ ] **Step 1: 写失败测试 — Registry 加载 attributes + gameplay_effects + skill_actions**

- [ ] **Step 2–4: 变更清单**

| 旧 | 新 |
|----|-----|
| `BuffDto` / `buffs/` | `GameplayEffectDefDto` / `gameplay_effects/` |
| `EffectDto` Damage/Heal/ModifyStat/ApplyBuff | 迁移为 `GameplayEffectDef` |
| `EffectDto` Draw/Discard/... | `SkillActionDto` / `skill_actions/` |
| 无 | `AttributeDefDto` / `attributes/` |
| 无 | `GameplayTagDefDto` / `tags/`（可选） |

- `TryGetBuff` → `TryGetGameplayEffect`（可保留 `[Obsolete]` 别名一版）
- Character/Enemy `BuffRefDto` → `GameplayEffectRefDto`

- [ ] **Step 5: 提交**

```powershell
git commit -m "refactor(content): GAS 内容管线替换 Buff/Effect 注册"
```

---

### Task 18：CardStatBlockDto 与基础值映射

**Files:**
- Modify: `Src/frame/content/definitions/CardStatBlockDto.cs`
- Modify: `Src/mod/combat/CharacterInstance.cs`
- Modify: `Src/frame/content/definitions/EnemyDto.cs`
- Create: `Src/frame/gas/AttributeContributionMapper.cs`
- Test: `Tests/kemo_card.Ui.Tests/Gas/AttributeContributionMapperTests.cs`

- [ ] **Step 1: 写失败测试 — 卡牌 stats 汇总为 attr 映射**

```csharp
[Test]
public void Card_stats_sum_into_character_base_attributes()
{
    var registry = /* strike card stats MaxHealth=4 PhysicalAttack=6 */;
    var totals = AttributeContributionMapper.SumFromDeck(deck, registry);
    Assert.That(totals[AttributeIds.MaxHealth], Is.EqualTo(4f));
}
```

- [ ] **Step 2–4: `CardStatBlockDto` 改为 `Dictionary<string, float> Attributes`；提供从旧字段迁移的静态映射（HpCap→MaxHealth 等）用于 base-game JSON 批量改写**

`EnemyDto`:

```csharp
[JsonPropertyName("baseAttributes")]
public Dictionary<string, float> BaseAttributes { get; init; } = [];

// maxHp 保留：加载后若 baseAttributes 无 MaxHealth 则注入 maxHp
```

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(gas): 卡牌与敌人属性贡献改为动态映射"
```

---

### Task 19：CombatEffectExecutor 分流 GE 与 SkillAction

**Files:**
- Modify: `Src/mod/combat/effects/CombatEffectExecutor.cs`
- Modify: `Src/mod/combat/statemachine/CombatStateMachine.cs`
- Modify: `Src/mod/combat/runtime/CombatSimulationFactory.cs`
- Test: 更新 `CombatEffectExecutorTests.cs`、`CombatSimulationIntegrationTests.cs`

- [ ] **Step 1: 写失败测试 — ApplyGameplayEffect 走 ASC**

```csharp
[Test]
public void ApplyGameplayEffect_ge_modifies_target_asc()
{
    // skill action kind ApplyGameplayEffect → target Asc
}
```

- [ ] **Step 2–4: 移除 `EEffectKind.Damage/Heal/ModifyStat/ApplyBuff` 分派；改为 `SkillActionExecutor` + `GameplayEffectApplicator`**

- `CombatSimulationFactory`：创建 ASC、`TeamMaxHealthCoordinator`、初始 Health=MaxHealth
- 回合推进：`CombatStateMachine` 敌方阶段结束 → 所有 ASC `OnTurnEnd/OnTurnStart`

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(combat): 效果执行器分流 GE 与 SkillAction"
```

---

### Task 20：base-game 内容迁移

**Files:**
- Migrate: `Config/mods/base-game/content/buffs/*.json` → `gameplay_effects/`
- Migrate: `Config/mods/base-game/content/effects/strike_damage.json` 等 → GE 或 skill_actions
- Add: `attributes/*.json`（Health、MaxHealth、PhysicalAttack、PhysicalDefense、Damage、Healing 等）
- Modify: `cards/strike.json` stats 为新格式
- Modify: `enemies/slime.json` baseAttributes

- [ ] **Step 1: 迁移 weak.json 示例**

`gameplay_effects/weak.json`:

```json
{
  "displayNameId": "ge.weak.name",
  "durationPolicy": "HasDuration",
  "durationTurns": 2,
  "stackingPolicy": "AggregateByTarget",
  "maxStacks": 3,
  "modifiers": [
    {
      "attributeId": "DamageTakenScale",
      "operation": "Add",
      "magnitude": { "kind": "Scalar", "scalar": 1 }
    }
  ],
  "grantedTags": ["debuff.weak"],
  "hooks": {
    "onApply": [{ "actionId": "weak_apply_marker" }]
  }
}
```

- [ ] **Step 2: 迁移 strike_damage 为 Instant Damage GE**

```json
{
  "durationPolicy": "Instant",
  "executions": [{ "kind": "Damage", "damageType": "Physical" }],
  "modifiers": [
    {
      "attributeId": "Damage",
      "operation": "Override",
      "magnitude": { "kind": "SetByCaller", "callerName": "Amount" }
    }
  ]
}
```

- [ ] **Step 3: 更新 skills 引用（effectId → gameplayEffectId / actionId）**

- [ ] **Step 4: 运行全量测试**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj -v n`

Expected: ALL PASS（含 Gas + Combat）

- [ ] **Step 5: 提交**

```powershell
git commit -m "content: 迁移 base-game 至 GAS GameplayEffect 与属性定义"
```

---

### Task 21：移除 CharacterAttributes 与遗留 Buff 路径

**Files:**
- Delete or obsolete: `Src/mod/combat/CharacterAttributes.cs`
- Update: 所有引用 `CharacterAttributes` / `BaseAttributes` / `TryGetBuff` 的测试与工厂
- Test: 全量回归

- [ ] **Step 1–4: 删除固定 struct；`CharacterBattleInstance` 用 ASC 读 MaxEnergy/InitialEnergy；更新 `CombatTestHelper.CreateFullRegistry` 支持 GameplayEffect/SkillAction/Attribute**

- [ ] **Step 5: 提交**

```powershell
git commit -m "refactor(combat): 移除 CharacterAttributes 与 BuffDto 遗留路径"
```

---

### Task 22：集成与确定性回归

**Files:**
- Create: `Tests/kemo_card.Ui.Tests/Gas/GasCombatIntegrationTests.cs`
- Update: `CombatSimulationIntegrationTests.cs`

- [ ] **Step 1: 写集成测试**

```csharp
[Test]
public void Full_battle_with_ge_damage_is_deterministic()
{
    var a = RunBattle(seed: 12345);
    var b = RunBattle(seed: 12345);
    Assert.That(a, Is.EqualTo(b));
}

[Test]
public void Weak_ge_increases_damage_taken_via_modifier()
{
    // 施加 weak → DamageTakenScale +1 → 受到更高伤害
}
```

- [ ] **Step 2: 运行全量测试**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj -v n`

Expected: ALL PASS

- [ ] **Step 3: 提交**

```powershell
git commit -m "test(gas): 添加 GAS 战斗集成与确定性回归测试"
```

---

## 规格自检

| 需求 | 对应 Task |
|------|-----------|
| 动态 AttributeSet | Task 1–2 |
| Add/Mul/Div/Override 聚合 | Task 2 |
| Magnitude 四来源 | Task 3 |
| ASC 骨架 | Task 4 |
| GameplayEffect 替代 BuffDto | Task 5–8, 17, 20 |
| Instant/Duration/Infinite/Periodic | Task 6–8 |
| 堆叠 | Task 6–7 |
| 全量 GameplayTags | Task 9–11 |
| Damage Execution | Task 12 |
| 三类 ASC | Task 13 |
| 派生 MaxHealth + delta_follow | Task 13 |
| 领域 Infinite GE | Task 15 |
| SkillAction 保留编排 | Task 16, 19 |
| Content 管线重构 | Task 11, 17–18, 20–21 |
| 规则引擎伤害接入 | Task 14 |
| 确定性/集成测试 | Task 22 |

**占位扫描:** 无 TBD；各 Task 含路径与代码骨架。

**类型一致性:** `AttributeIds`、`GameplayEffectSpec`、`SkillActionRefDto`、`ApplyGameplayEffect` 全计划统一。

---

## 执行方式

Plan complete and saved to `Doc/superpowers/plans/2026-06-22-gas-buff-attribute-system-implementation-plan.md`.

**两种执行方式：**

1. **Subagent-Driven（推荐）** — 每个 Task 派发独立 subagent，Task 间 review，迭代快
2. **Inline Execution** — 本会话用 executing-plans 按 Task 批量执行，检查点 review

**请选择一种方式。**
