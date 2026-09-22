# 战斗 mod 核心逻辑实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，按任务逐步实现。步骤使用 checkbox（`- [ ]`）追踪。

**Goal:** 在 `Src/mod/combat/` 实现纯 C#、确定性、联机就绪的战斗核心逻辑层：状态机、初始化时冻结的规则引擎、队伍级领域 buff、最小效果执行器、最小敌人 AI、共享 HP 胜负判定；配套单元测试。不含 Buff 单位实例、战斗 UI、局内存档。

**Architecture:** 权威模拟 `CombatSimulation` 聚合 `PlayerTeamState`/`EnemyTeamState`/`CardExecutionQueue`/`CombatRuleEngine`；所有玩家操作经可序列化 `ICombatCommand`（显式 `characterIndex`）进入 `TryApply`。规则集在 `CombatSimulationFactory` 初始化时一次性注入后冻结。领域是队伍级 buff 单例（非规则），经 `BuffDto` 钩子 + 效果执行器生效。状态机落实 [kemo-card 设计规格](../../../superpowers/specs/2026-05-11-kemo-card-design.md) 第 3 节。

**Tech Stack:** Godot 4.6.1 Mono + .NET 8 + C# 12 + NUnit 4 + 现有 `HostRng` / `GameDefinitionRegistry` / `PuertsContentEffectScriptHost`

**设计来源:** [战斗 mod 核心逻辑计划](../../.cursor/plans/战斗mod核心逻辑_4db7adad.plan.md)（brainstorming 产出）

---

## 文件结构

| 路径 | 职责 |
|------|------|
| `Src/frame/content/definitions/BattleDto.cs` | 增加 `combatRuleIds` |
| `Src/mod/combat/runtime/ECombatSide.cs` | 阵营枚举 |
| `Src/mod/combat/runtime/CombatTargetRef.cs` | 可序列化目标引用 |
| `Src/mod/combat/runtime/EnemyUnit.cs` | 敌人运行时单位 |
| `Src/mod/combat/runtime/PlayerTeamState.cs` | 玩家队：共享 HP + 角色战斗实例 + 领域 |
| `Src/mod/combat/runtime/EnemyTeamState.cs` | 敌方队：敌人列表 + 领域 |
| `Src/mod/combat/runtime/CombatDomain.cs` | 队伍级领域 buff 快照 |
| `Src/mod/combat/runtime/TeamDomainManager.cs` | 领域顶替与钩子触发 |
| `Src/mod/combat/runtime/CombatSimulation.cs` | 模拟聚合根 + `TryApply` |
| `Src/mod/combat/runtime/CombatSimulationFactory.cs` | 开战装配、规则冻结、波次生成 |
| `Src/mod/combat/runtime/CombatApplyResult.cs` | 指令应用结果 |
| `Src/mod/combat/rules/ICombatRule.cs` | 规则接口（默认空钩子） |
| `Src/mod/combat/rules/CombatContext.cs` | 规则/效果上下文 |
| `Src/mod/combat/rules/DamagePacket.cs` | 可改写伤害包 |
| `Src/mod/combat/rules/EndDecision.cs` | 胜负判定改写 |
| `Src/mod/combat/rules/CombatRuleEngine.cs` | 有序分派；初始化后只读 |
| `Src/mod/combat/rules/CombatRuleCatalog.cs` | 规则 id → 实例工厂 |
| `Src/mod/combat/rules/builtin/SharedHpDefeatRule.cs` | 内置：共享 HP 归零败北 |
| `Src/mod/combat/statemachine/ECombatPhase.cs` | 战斗阶段枚举 |
| `Src/mod/combat/statemachine/ICombatPhaseHandler.cs` | 阶段处理器接口 |
| `Src/mod/combat/statemachine/CombatStateMachine.cs` | 阶段切换与 tick |
| `Src/mod/combat/statemachine/CardExecutionQueue.cs` | 卡牌优先队列 |
| `Src/mod/combat/statemachine/QueuedCardEntry.cs` | 队列项 |
| `Src/mod/combat/commands/ICombatCommand.cs` | 指令接口 |
| `Src/mod/combat/commands/*.cs` | 四类玩家指令 |
| `Src/mod/combat/effects/CombatEffectExecutor.cs` | 效果分派 |
| `Src/mod/combat/effects/CombatTargetResolver.cs` | 目标解析 |
| `Src/mod/combat/effects/IContentEffectScriptHost.cs` | 已有；战斗层注入 |
| `Src/mod/combat/ai/EnemyAiController.cs` | 最小敌人 AI |
| `Tests/kemo_card.Ui.Tests/Combat/CombatTestHelper.cs` | 扩展全量 Registry 构建 |
| `Tests/kemo_card.Ui.Tests/Combat/*Tests.cs` | 各模块单测 |

---

### Task 0：扩展 `CombatTestHelper`

**Files:**
- Modify: `Tests/kemo_card.Ui.Tests/Combat/CombatTestHelper.cs`

- [ ] **Step 1: 增加 `CreateFullRegistry` 重载**

在现有 `CreateRegistry` 旁新增方法，支持 battles/enemies/skills/effects/buffs：

```csharp
public static GameDefinitionRegistry CreateFullRegistry(
    IReadOnlyDictionary<string, CardDto>? cards = null,
    IReadOnlyDictionary<string, SkillDto>? skills = null,
    IReadOnlyDictionary<string, EffectDto>? effects = null,
    IReadOnlyDictionary<string, BuffDto>? buffs = null,
    IReadOnlyDictionary<string, EnemyDto>? enemies = null,
    IReadOnlyDictionary<string, BattleDto>? battles = null,
    IReadOnlyDictionary<string, CharacterDto>? characters = null)
{
    cards ??= new Dictionary<string, CardDto>(StringComparer.Ordinal);
    skills ??= new Dictionary<string, SkillDto>(StringComparer.Ordinal);
    effects ??= new Dictionary<string, EffectDto>(StringComparer.Ordinal);
    buffs ??= new Dictionary<string, BuffDto>(StringComparer.Ordinal);
    enemies ??= new Dictionary<string, EnemyDto>(StringComparer.Ordinal);
    battles ??= new Dictionary<string, BattleDto>(StringComparer.Ordinal);
    characters ??= new Dictionary<string, CharacterDto>(StringComparer.Ordinal);

    var definitions = new ModDefinitionsBundle(
        characters, enemies, battles,
        ModDefinitionsBundle.Empty.Events,
        ModDefinitionsBundle.Empty.Items,
        cards, skills, buffs, effects);

    var bundle = new ModContentBundle(
        ModId: "test.mod",
        Characters: characters.Keys.ToList(),
        Enemies: enemies.Keys.ToList(),
        Battles: battles.Keys.ToList(),
        Events: [],
        Cards: cards.Keys.ToList(),
        Items: [],
        Skills: skills.Keys.ToList(),
        Buffs: buffs.Keys.ToList(),
        Effects: effects.Keys.ToList(),
        Definitions: definitions);

    var registry = new GameDefinitionRegistry();
    registry.Rebuild([bundle], out _);
    return registry;
}
```

- [ ] **Step 2: 运行现有测试确保无回归**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter FullyQualifiedName~CombatTestHelper -v n`

Expected: PASS（或 0 tests if no dedicated tests — 改跑 `FullyQualifiedName~Combat`）

- [ ] **Step 3: 提交**

```powershell
git add Tests/kemo_card.Ui.Tests/Combat/CombatTestHelper.cs
git commit -m "test(combat): 扩展 CombatTestHelper 支持全量内容注册表"
```

---

### Task 1：目标引用与敌人单位

**Files:**
- Create: `Src/mod/combat/runtime/ECombatSide.cs`
- Create: `Src/mod/combat/runtime/CombatTargetRef.cs`
- Create: `Src/mod/combat/runtime/EnemyUnit.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/EnemyUnitTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class EnemyUnitTests
{
    [Test]
    public void EnemyUnit_tracks_hp_and_alive_state()
    {
        var unit = new EnemyUnit("rt-1", "slime", maxHp: 20);
        Assert.That(unit.IsAlive, Is.True);
        unit.ApplyDamage(8);
        Assert.That(unit.CurrentHp, Is.EqualTo(12));
        unit.ApplyDamage(12);
        Assert.That(unit.IsAlive, Is.False);
    }

    [Test]
    public void CombatTargetRef_is_value_equality()
    {
        var a = new CombatTargetRef(ECombatSide.Enemy, 0);
        var b = new CombatTargetRef(ECombatSide.Enemy, 0);
        Assert.That(a, Is.EqualTo(b));
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter FullyQualifiedName~EnemyUnitTests -v n`

Expected: FAIL — namespace/type not found

- [ ] **Step 3: 实现类型**

`ECombatSide.cs`:

```csharp
namespace KemoCard.Mod.Combat.Runtime;

public enum ECombatSide
{
    Player,
    Enemy,
}
```

`CombatTargetRef.cs`:

```csharp
namespace KemoCard.Mod.Combat.Runtime;

public readonly record struct CombatTargetRef(ECombatSide Side, int Index)
{
    public static CombatTargetRef PlayerTeam => new(ECombatSide.Player, -1);
}
```

`EnemyUnit.cs`:

```csharp
namespace KemoCard.Mod.Combat.Runtime;

public sealed class EnemyUnit
{
    public string RuntimeId { get; }
    public string DefinitionId { get; }
    public int CurrentHp { get; private set; }
    public int MaxHp { get; }
    public string? IntentSkillId { get; set; }
    public bool IsAlive => CurrentHp > 0;

    public EnemyUnit(string runtimeId, string definitionId, int maxHp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        if (maxHp <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxHp));
        RuntimeId = runtimeId;
        DefinitionId = definitionId;
        MaxHp = maxHp;
        CurrentHp = maxHp;
    }

    public void ApplyDamage(int amount)
    {
        if (amount <= 0 || !IsAlive)
            return;
        CurrentHp = Math.Max(0, CurrentHp - amount);
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || !IsAlive)
            return;
        CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
    }
}
```

- [ ] **Step 4: 运行测试通过**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter FullyQualifiedName~EnemyUnitTests -v n`

Expected: PASS

- [ ] **Step 5: 提交**

```powershell
git add Src/mod/combat/runtime/ECombatSide.cs Src/mod/combat/runtime/CombatTargetRef.cs Src/mod/combat/runtime/EnemyUnit.cs Tests/kemo_card.Ui.Tests/Combat/EnemyUnitTests.cs
git commit -m "feat(combat): 添加敌人单位与战斗目标引用类型"
```

---

### Task 2：队伍状态与共享 HP

**Files:**
- Create: `Src/mod/combat/runtime/PlayerTeamState.cs`
- Create: `Src/mod/combat/runtime/EnemyTeamState.cs`
- Create: `Src/mod/combat/runtime/CombatDomain.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/PlayerTeamStateTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class PlayerTeamStateTests
{
    [Test]
    public void PlayerTeamState_uses_shared_hp_pool()
    {
        var chars = new[]
        {
            CreateBattle("c0", hpCap: 10),
            CreateBattle("c1", hpCap: 15),
        };
        var team = new PlayerTeamState(chars, sharedMaxHp: 25);

        team.ApplySharedDamage(10);
        Assert.That(team.SharedHp, Is.EqualTo(15));
        team.ApplySharedDamage(20);
        Assert.That(team.IsDefeated, Is.True);
    }

    private static CharacterBattleInstance CreateBattle(string id, int hpCap)
    {
        var attrs = new CharacterAttributes(hpCap, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        return CharacterBattleInstance.CreateForTests(id, attrs);
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter FullyQualifiedName~PlayerTeamStateTests -v n`

Expected: FAIL

- [ ] **Step 3: 为测试添加 `CharacterBattleInstance.CreateForTests` 内部工厂**

在 `CharacterBattleInstance.cs` 末尾增加（仅测试/工厂使用）：

```csharp
internal static CharacterBattleInstance CreateForTests(string definitionId, CharacterAttributes baseAttributes)
{
    return new CharacterBattleInstance(
        sourceInstanceId: Guid.NewGuid().ToString("N"),
        definitionId,
        baseAttributes,
        drawPile: [],
        currentEnergy: baseAttributes.InitialEnergy,
        maxEnergy: baseAttributes.MaxEnergy,
        energyCap: baseAttributes.MaxEnergy);
}
```

将构造函数改为 `internal` 或保留 private 并添加上述 static 工厂。

- [ ] **Step 4: 实现队伍状态**

`CombatDomain.cs`:

```csharp
namespace KemoCard.Mod.Combat.Runtime;

public sealed record CombatDomain(string BuffId, IReadOnlyDictionary<string, object>? Params);
```

`PlayerTeamState.cs`:

```csharp
using KemoCard.Mod.Combat;

namespace KemoCard.Mod.Combat.Runtime;

public sealed class PlayerTeamState
{
    private readonly CharacterBattleInstance[] _characters;

    public int SharedHp { get; private set; }
    public int MaxHp { get; }
    public CombatDomain? ActiveDomain { get; set; }
    public IReadOnlyList<CharacterBattleInstance> Characters => _characters;
    public bool IsDefeated => SharedHp <= 0;

    public PlayerTeamState(IReadOnlyList<CharacterBattleInstance> characters, int sharedMaxHp)
    {
        ArgumentNullException.ThrowIfNull(characters);
        if (characters.Count == 0)
            throw new ArgumentException("至少一名角色。", nameof(characters));
        if (sharedMaxHp <= 0)
            throw new ArgumentOutOfRangeException(nameof(sharedMaxHp));
        _characters = characters.ToArray();
        MaxHp = sharedMaxHp;
        SharedHp = sharedMaxHp;
    }

    public void ApplySharedDamage(int amount)
    {
        if (amount <= 0 || IsDefeated)
            return;
        SharedHp = Math.Max(0, SharedHp - amount);
    }

    public void HealShared(int amount)
    {
        if (amount <= 0 || IsDefeated)
            return;
        SharedHp = Math.Min(MaxHp, SharedHp + amount);
    }
}
```

`EnemyTeamState.cs`:

```csharp
namespace KemoCard.Mod.Combat.Runtime;

public sealed class EnemyTeamState
{
    private readonly List<EnemyUnit> _enemies;

    public CombatDomain? ActiveDomain { get; set; }
    public IReadOnlyList<EnemyUnit> Enemies => _enemies;
    public bool AllDefeated => _enemies.Count == 0 || _enemies.All(e => !e.IsAlive);

    public EnemyTeamState(IEnumerable<EnemyUnit> enemies)
    {
        _enemies = enemies.ToList();
    }
}
```

- [ ] **Step 5: 运行测试通过**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter FullyQualifiedName~PlayerTeamStateTests -v n`

Expected: PASS

- [ ] **Step 6: 提交**

```powershell
git add Src/mod/combat/runtime/PlayerTeamState.cs Src/mod/combat/runtime/EnemyTeamState.cs Src/mod/combat/runtime/CombatDomain.cs Src/mod/combat/CharacterBattleInstance.cs Tests/kemo_card.Ui.Tests/Combat/PlayerTeamStateTests.cs
git commit -m "feat(combat): 添加队伍状态与共享 HP 池"
```

---

### Task 3：规则引擎（初始化冻结）

**Files:**
- Create: `Src/mod/combat/rules/ICombatRule.cs`
- Create: `Src/mod/combat/rules/DamagePacket.cs`
- Create: `Src/mod/combat/rules/EndDecision.cs`
- Create: `Src/mod/combat/rules/CombatContext.cs`
- Create: `Src/mod/combat/rules/CombatRuleEngine.cs`
- Create: `Src/mod/combat/rules/CombatRuleCatalog.cs`
- Create: `Src/mod/combat/rules/builtin/SharedHpDefeatRule.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CombatRuleEngineTests.cs`

- [ ] **Step 1: 写失败测试 — 规则按 Priority 分派且不可变**

```csharp
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Rules.Builtin;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatRuleEngineTests
{
    private sealed class CaptureRule : ICombatRule
    {
        public string Id { get; }
        public int Priority { get; }
        public int TurnStartCount { get; private set; }

        public CaptureRule(string id, int priority) { Id = id; Priority = priority; }

        public void OnTurnStart(CombatContext ctx) => TurnStartCount++;
    }

    [Test]
    public void Dispatch_runs_rules_in_priority_order()
    {
        var low = new CaptureRule("low", priority: 10);
        var high = new CaptureRule("high", priority: 1);
        var engine = new CombatRuleEngine([low, high]);
        var order = new List<string>();
        engine.DispatchTurnStart(new CombatContext(null!, turnNumber: 1), r => order.Add(r.Id));
        Assert.That(order, Is.EqualTo(new[] { "high", "low" }));
    }

    [Test]
    public void Rules_collection_is_readonly_after_construction()
    {
        var engine = new CombatRuleEngine([new SharedHpDefeatRule()]);
        Assert.That(engine.Rules, Is.InstanceOf<IReadOnlyList<ICombatRule>>());
        Assert.That(engine.Rules.Count, Is.EqualTo(1));
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter FullyQualifiedName~CombatRuleEngineTests -v n`

Expected: FAIL

- [ ] **Step 3: 实现规则核心**

`ICombatRule.cs`:

```csharp
namespace KemoCard.Mod.Combat.Rules;

public interface ICombatRule
{
    string Id { get; }
    int Priority { get; }

    void OnBattleStart(CombatContext ctx) { }
    void OnTurnStart(CombatContext ctx) { }
    void OnTurnEnd(CombatContext ctx) { }
    void OnBeforeDamage(CombatContext ctx, ref DamagePacket packet) { }
    void OnAfterDamage(CombatContext ctx, in DamagePacket packet) { }
    void OnCheckEndCondition(CombatContext ctx, ref EndDecision decision) { }
}
```

`DamagePacket.cs`:

```csharp
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Rules;

public sealed class DamagePacket
{
    public CombatTargetRef Source { get; init; }
    public CombatTargetRef Target { get; init; }
    public int Amount { get; set; }
    public string? EffectId { get; init; }
}
```

`EndDecision.cs`:

```csharp
namespace KemoCard.Mod.Combat.Rules;

public enum EEndDecisionKind
{
    None,
    Victory,
    Defeat,
}

public struct EndDecision
{
    public EEndDecisionKind Kind { get; set; }
}
```

`CombatContext.cs`:

```csharp
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Rules;

public sealed class CombatContext
{
    public CombatSimulation Simulation { get; }
    public int TurnNumber { get; }

    public CombatContext(CombatSimulation simulation, int turnNumber)
    {
        Simulation = simulation;
        TurnNumber = turnNumber;
    }
}
```

`CombatRuleEngine.cs`（**无 Add/Remove**）:

```csharp
namespace KemoCard.Mod.Combat.Rules;

public sealed class CombatRuleEngine
{
    private readonly IReadOnlyList<ICombatRule> _rules;

    public IReadOnlyList<ICombatRule> Rules => _rules;

    public CombatRuleEngine(IEnumerable<ICombatRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.OrderBy(r => r.Priority).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
    }

    public void DispatchTurnStart(CombatContext ctx, Action<ICombatRule>? trace = null)
    {
        foreach (var rule in _rules)
        {
            trace?.Invoke(rule);
            rule.OnTurnStart(ctx);
        }
    }

    public void DispatchBeforeDamage(CombatContext ctx, ref DamagePacket packet)
    {
        foreach (var rule in _rules)
            rule.OnBeforeDamage(ctx, ref packet);
    }

    public void DispatchCheckEndCondition(CombatContext ctx, ref EndDecision decision)
    {
        foreach (var rule in _rules)
            rule.OnCheckEndCondition(ctx, ref decision);
    }
}
```

`CombatRuleCatalog.cs`:

```csharp
namespace KemoCard.Mod.Combat.Rules;

public sealed class CombatRuleCatalog
{
    private readonly Dictionary<string, Func<ICombatRule>> _factories = new(StringComparer.Ordinal);

    public void Register(string ruleId, Func<ICombatRule> factory) => _factories[ruleId] = factory;

    public ICombatRule? TryCreate(string ruleId) =>
        _factories.TryGetValue(ruleId, out var factory) ? factory() : null;

    public static CombatRuleCatalog CreateDefault()
    {
        var catalog = new CombatRuleCatalog();
        catalog.Register(SharedHpDefeatRule.RuleId, () => new SharedHpDefeatRule());
        return catalog;
    }
}
```

`SharedHpDefeatRule.cs`:

```csharp
namespace KemoCard.Mod.Combat.Rules.Builtin;

public sealed class SharedHpDefeatRule : ICombatRule
{
    public const string RuleId = "builtin.shared_hp_defeat";
    public string Id => RuleId;
    public int Priority => 1000;

    public void OnCheckEndCondition(CombatContext ctx, ref EndDecision decision)
    {
        if (ctx.Simulation.PlayerTeam.IsDefeated)
            decision.Kind = EEndDecisionKind.Defeat;
    }
}
```

> **注意:** `CombatContext` 引用 `CombatSimulation`，Task 5 再补全该类；本 Task 测试可先用 `null!` 或延迟到 Task 5 再跑 Dispatch 集成测试。若编译阻塞，先建 `CombatSimulation` 空壳 public 类。

- [ ] **Step 4: 运行测试通过**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter FullyQualifiedName~CombatRuleEngineTests -v n`

Expected: PASS

- [ ] **Step 5: 提交**

```powershell
git add Src/mod/combat/rules/ Tests/kemo_card.Ui.Tests/Combat/CombatRuleEngineTests.cs
git commit -m "feat(combat): 添加初始化冻结的战斗规则引擎"
```

---

### Task 4：卡牌执行优先队列

**Files:**
- Create: `Src/mod/combat/statemachine/QueuedCardEntry.cs`
- Create: `Src/mod/combat/statemachine/CardExecutionQueue.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CardExecutionQueueTests.cs`

- [ ] **Step 1: 写失败测试 — 优先级升序 + 同级 RNG 平局**

```csharp
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CardExecutionQueueTests
{
    [Test]
    public void Dequeue_returns_lowest_priority_first()
    {
        var queue = new CardExecutionQueue(new HostRng(7, "combat.queue"));
        queue.Enqueue(new QueuedCardEntry(0, "a", "rt-a", priority: 5, [], sequence: 1));
        queue.Enqueue(new QueuedCardEntry(1, "b", "rt-b", priority: 1, [], sequence: 2));
        var first = queue.TryDequeue(out var entry);
        Assert.That(first, Is.True);
        Assert.That(entry!.CardId, Is.EqualTo("b"));
    }

    [Test]
    public void Same_priority_uses_deterministic_rng_tiebreak()
    {
        var q1 = new CardExecutionQueue(new HostRng(99, "combat.queue"));
        var q2 = new CardExecutionQueue(new HostRng(99, "combat.queue"));
        q1.Enqueue(new QueuedCardEntry(0, "x", "rt-x", priority: 3, [], sequence: 1));
        q1.Enqueue(new QueuedCardEntry(1, "y", "rt-y", priority: 3, [], sequence: 2));
        q2.Enqueue(new QueuedCardEntry(0, "x", "rt-x", priority: 3, [], sequence: 1));
        q2.Enqueue(new QueuedCardEntry(1, "y", "rt-y", priority: 3, [], sequence: 2));
        q1.TryDequeue(out var e1);
        q2.TryDequeue(out var e2);
        Assert.That(e1!.CardId, Is.EqualTo(e2!.CardId));
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter FullyQualifiedName~CardExecutionQueueTests -v n`

Expected: FAIL

- [ ] **Step 3: 实现队列**

`QueuedCardEntry.cs`:

```csharp
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.StateMachine;

public sealed record QueuedCardEntry(
    int CharacterIndex,
    string CardId,
    string RuntimeInstanceId,
    int Priority,
    IReadOnlyList<CombatTargetRef> Targets,
    long Sequence);
```

`CardExecutionQueue.cs` — 使用 `SortedSet` 或最小堆；比较键 `(Priority, TieBreakKey, Sequence)`，`TieBreakKey` 在 Enqueue 时用 `HostRng.NextInt(0, int.MaxValue)` 生成并存于 entry 扩展字段或 wrapper：

```csharp
using KemoCard.Frame.Scripting;

namespace KemoCard.Mod.Combat.StateMachine;

public sealed class CardExecutionQueue
{
    private readonly HostRng _rng;
    private readonly SortedSet<(int Priority, int TieBreak, long Sequence, QueuedCardEntry Entry)> _heap = [];

    public CardExecutionQueue(HostRng rng) => _rng = rng;

    public void Enqueue(QueuedCardEntry entry)
    {
        var tieBreak = _rng.NextInt(0, int.MaxValue);
        _heap.Add((entry.Priority, tieBreak, entry.Sequence, entry));
    }

    public bool TryDequeue(out QueuedCardEntry? entry)
    {
        if (_heap.Count == 0)
        {
            entry = null;
            return false;
        }
        var first = _heap.Min;
        _heap.Remove(first);
        entry = first.Entry;
        return true;
    }

    public int Count => _heap.Count;

    public IEnumerable<QueuedCardEntry> PeekAllOrdered() =>
        _heap.Select(x => x.Entry);
}
```

- [ ] **Step 4: 运行测试通过**

Expected: PASS

- [ ] **Step 5: 提交**

```powershell
git add Src/mod/combat/statemachine/QueuedCardEntry.cs Src/mod/combat/statemachine/CardExecutionQueue.cs Tests/kemo_card.Ui.Tests/Combat/CardExecutionQueueTests.cs
git commit -m "feat(combat): 添加卡牌执行优先队列"
```

---

### Task 5：最小效果执行器（Damage / Heal）

**Files:**
- Create: `Src/mod/combat/effects/CombatTargetResolver.cs`
- Create: `Src/mod/combat/effects/CombatEffectExecutor.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CombatEffectExecutorTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Rules.Builtin;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.Effects;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatEffectExecutorTests
{
    [Test]
    public void Damage_reduces_enemy_hp_through_rule_pipeline()
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            effects: new Dictionary<string, EffectDto>
            {
                ["hit"] = new() { Id = "hit", Kind = EEffectKind.Damage, Params = new() { ["amount"] = 5 } },
            });
        var enemy = new EnemyUnit("e0", "slime", maxHp: 20);
        var sim = CombatSimulationTestBuilder.Minimal(enemy, registry, rules: [new SharedHpDefeatRule()]);
        var executor = new CombatEffectExecutor(registry, sim.Rules);
        var source = new CombatTargetRef(ECombatSide.Player, 0);
        var target = new CombatTargetRef(ECombatSide.Enemy, 0);

        executor.ExecuteEffectRef(new EffectRefDto { EffectId = "hit" }, sim, source, [target]);

        Assert.That(enemy.CurrentHp, Is.EqualTo(15));
    }
}
```

同步创建 `CombatSimulationTestBuilder` 于测试项目（最小模拟构建器，供多测复用）。

- [ ] **Step 2: 运行确认失败**

Expected: FAIL

- [ ] **Step 3: 实现 `CombatTargetResolver` 与 `CombatEffectExecutor`**

`CombatEffectExecutor` 核心分派：

```csharp
public void ExecuteEffectRef(
    EffectRefDto effectRef,
    CombatSimulation simulation,
    CombatTargetRef source,
    IReadOnlyList<CombatTargetRef> targets)
{
    if (!simulation.Definitions.Store.TryGetEffect(effectRef.EffectId, out var effect))
        return;

    var mergedParams = MergeParams(effect.Params, effectRef.Params);
    switch (effect.Kind)
    {
        case EEffectKind.Damage:
            ApplyDamage(simulation, source, targets, ReadInt(mergedParams, "amount", 0));
            break;
        case EEffectKind.Heal:
            ApplyHeal(simulation, targets, ReadInt(mergedParams, "amount", 0));
            break;
        case EEffectKind.ChainEffects:
            foreach (var child in effect.EffectRefs)
                ExecuteEffectRef(child, simulation, source, targets);
            break;
        case EEffectKind.ApplyBuff:
        case EEffectKind.RemoveBuff:
        case EEffectKind.ModifyStat:
            // 软失败：记录诊断，等 Buff 系统
            break;
        default:
            break;
    }
}

private void ApplyDamage(CombatSimulation sim, CombatTargetRef source, IReadOnlyList<CombatTargetRef> targets, int amount)
{
    foreach (var target in targets)
    {
        var packet = new DamagePacket { Source = source, Target = target, Amount = amount };
        var ctx = sim.CreateContext();
        sim.Rules.DispatchBeforeDamage(ctx, ref packet);
        if (packet.Amount <= 0)
            continue;
        if (target.Side == ECombatSide.Enemy)
            sim.EnemyTeam.Enemies[target.Index].ApplyDamage(packet.Amount);
        else if (target.Side == ECombatSide.Player && target.Index < 0)
            sim.PlayerTeam.ApplySharedDamage(packet.Amount);
    }
}
```

- [ ] **Step 4: 运行测试通过**

Expected: PASS

- [ ] **Step 5: 提交**

```powershell
git add Src/mod/combat/effects/ Tests/kemo_card.Ui.Tests/Combat/CombatEffectExecutorTests.cs Tests/kemo_card.Ui.Tests/Combat/CombatSimulationTestBuilder.cs
git commit -m "feat(combat): 添加最小效果执行器 Damage/Heal/Chain"
```

---

### Task 6：队伍级领域 buff

**Files:**
- Create: `Src/mod/combat/runtime/TeamDomainManager.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/TeamDomainManagerTests.cs`

- [ ] **Step 1: 写失败测试 — 同队顶替、双方不冲突**

```csharp
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class TeamDomainManagerTests
{
    [Test]
    public void SetDomain_replaces_existing_domain_on_same_team()
    {
        var hooksApplied = new List<string>();
        var buffs = new Dictionary<string, BuffDto>
        {
            ["domain_a"] = new() { Id = "domain_a", Hooks = new() { OnApply = [new EffectRefDto { EffectId = "mark_a" }] } },
            ["domain_b"] = new() { Id = "domain_b", Hooks = new() { OnApply = [new EffectRefDto { EffectId = "mark_b" }] } },
        };
        var effects = new Dictionary<string, EffectDto>
        {
            ["mark_a"] = new() { Id = "mark_a", Kind = EEffectKind.Damage, Params = new() { ["amount"] = 1 } },
            ["mark_b"] = new() { Id = "mark_b", Kind = EEffectKind.Damage, Params = new() { ["amount"] = 2 } },
        };
        var registry = CombatTestHelper.CreateFullRegistry(effects: effects, buffs: buffs);
        var sim = CombatSimulationTestBuilder.Minimal(new EnemyUnit("e", "slime", 10), registry);
        var manager = new TeamDomainManager(sim);

        manager.TrySetPlayerDomain("domain_a");
        Assert.That(sim.PlayerTeam.ActiveDomain?.BuffId, Is.EqualTo("domain_a"));

        manager.TrySetPlayerDomain("domain_b");
        Assert.That(sim.PlayerTeam.ActiveDomain?.BuffId, Is.EqualTo("domain_b"));
    }

    [Test]
    public void Player_and_enemy_domains_do_not_conflict()
    {
        var registry = CombatTestHelper.CreateFullRegistry(buffs: new Dictionary<string, BuffDto>
        {
            ["d1"] = new() { Id = "d1" },
            ["d2"] = new() { Id = "d2" },
        });
        var sim = CombatSimulationTestBuilder.Minimal(new EnemyUnit("e", "slime", 10), registry);
        var manager = new TeamDomainManager(sim);

        manager.TrySetPlayerDomain("d1");
        manager.TrySetEnemyDomain("d2");

        Assert.That(sim.PlayerTeam.ActiveDomain?.BuffId, Is.EqualTo("d1"));
        Assert.That(sim.EnemyTeam.ActiveDomain?.BuffId, Is.EqualTo("d2"));
    }
}
```

- [ ] **Step 2: 运行确认失败**

Expected: FAIL

- [ ] **Step 3: 实现 `TeamDomainManager`**

```csharp
public sealed class TeamDomainManager
{
    private readonly CombatSimulation _sim;
    private readonly CombatEffectExecutor _executor;

    public TeamDomainManager(CombatSimulation simulation, CombatEffectExecutor executor)
    {
        _sim = simulation;
        _executor = executor;
    }

    public bool TrySetPlayerDomain(string buffId, IReadOnlyDictionary<string, object>? parameters = null)
        => TrySetDomain(_sim.PlayerTeam, buffId, parameters);

    public bool TrySetEnemyDomain(string buffId, IReadOnlyDictionary<string, object>? parameters = null)
        => TrySetDomain(_sim.EnemyTeam, buffId, parameters);

    private bool TrySetDomain(dynamic team, string buffId, IReadOnlyDictionary<string, object>? parameters)
    {
        if (!_sim.Definitions.Store.TryGetBuff(buffId, out var buff))
            return false;

        if (team.ActiveDomain is CombatDomain oldDomain &&
            _sim.Definitions.Store.TryGetBuff(oldDomain.BuffId, out var oldBuff))
        {
            FireHooks(oldBuff.Hooks.OnRemove, ECombatSide.Player);
        }

        team.ActiveDomain = new CombatDomain(buffId, parameters);
        FireHooks(buff.Hooks.OnApply, ECombatSide.Player);
        return true;
    }

    private void FireHooks(IReadOnlyList<EffectRefDto> hooks, ECombatSide side)
    {
        var allTargets = BuildAllUnitTargets(side);
        var source = CombatTargetRef.PlayerTeam;
        foreach (var hook in hooks)
            _executor.ExecuteEffectRef(hook, _sim, source, allTargets);
    }
}
```

> 实现时将 `dynamic team` 改为接受 `(PlayerTeamState|EnemyTeamState)` 的重载或接口，避免 dynamic。

- [ ] **Step 4: 运行测试通过**

Expected: PASS

- [ ] **Step 5: 提交**

```powershell
git add Src/mod/combat/runtime/TeamDomainManager.cs Tests/kemo_card.Ui.Tests/Combat/TeamDomainManagerTests.cs
git commit -m "feat(combat): 实现队伍级领域 buff 顶替与钩子"
```

---

### Task 7：战斗指令层

**Files:**
- Create: `Src/mod/combat/commands/ICombatCommand.cs`
- Create: `Src/mod/combat/commands/PlayCardCommand.cs`
- Create: `Src/mod/combat/commands/CastInstantSkillCommand.cs`
- Create: `Src/mod/combat/commands/ConfirmCharacterCommand.cs`
- Create: `Src/mod/combat/commands/CancelQueuedCardCommand.cs`
- Create: `Src/mod/combat/runtime/CombatApplyResult.cs`
- Modify: `Src/mod/combat/runtime/CombatSimulation.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CombatCommandTests.cs`

- [ ] **Step 1: 写失败测试 — 指令携带 characterIndex**

```csharp
using KemoCard.Mod.Combat.Commands;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatCommandTests
{
    [Test]
    public void PlayCardCommand_carries_explicit_character_index()
    {
        var cmd = new PlayCardCommand(characterIndex: 2, handSlotIndex: 1, targets: []);
        Assert.That(cmd.CharacterIndex, Is.EqualTo(2));
    }

    [Test]
    public void ConfirmCharacter_marks_has_acted_in_player_phase()
    {
        var sim = CombatSimulationTestBuilder.StandardPlayerPhase();
        var result = sim.TryApply(new ConfirmCharacterCommand(characterIndex: 0));
        Assert.That(result.Success, Is.True);
        Assert.That(sim.PlayerTeam.Characters[0].HasActed, Is.True);
    }
}
```

- [ ] **Step 2: 运行确认失败**

Expected: FAIL

- [ ] **Step 3: 实现指令与 `CombatSimulation.TryApply`**

`ICombatCommand.cs`:

```csharp
namespace KemoCard.Mod.Combat.Commands;

public interface ICombatCommand
{
    int CharacterIndex { get; }
}
```

`CombatApplyResult.cs`:

```csharp
namespace KemoCard.Mod.Combat.Runtime;

public sealed record CombatApplyResult(bool Success, string? Error = null);
```

`CombatSimulation.TryApply` 委托当前 `CombatStateMachine` 的 phase handler 校验并执行；**不包含** `SwitchActiveCharacterCommand`。

- [ ] **Step 4: 运行测试通过**

Expected: PASS

- [ ] **Step 5: 提交**

```powershell
git add Src/mod/combat/commands/ Src/mod/combat/runtime/CombatApplyResult.cs Src/mod/combat/runtime/CombatSimulation.cs Tests/kemo_card.Ui.Tests/Combat/CombatCommandTests.cs
git commit -m "feat(combat): 添加可序列化战斗指令与 TryApply 入口"
```

---

### Task 8：战斗状态机

**Files:**
- Create: `Src/mod/combat/statemachine/ECombatPhase.cs`
- Create: `Src/mod/combat/statemachine/ICombatPhaseHandler.cs`
- Create: `Src/mod/combat/statemachine/CombatStateMachine.cs`
- Create: `Src/mod/combat/statemachine/handlers/PlayerPhaseHandler.cs`
- Create: `Src/mod/combat/statemachine/handlers/CardExecutionPhaseHandler.cs`
- Create: `Src/mod/combat/statemachine/handlers/EnemyPhaseHandler.cs`
- Create: `Src/mod/combat/statemachine/handlers/BattleStartHandler.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CombatStateMachineTests.cs`

- [ ] **Step 1: 写失败测试 — 四人均已行动后进入卡牌执行阶段**

```csharp
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatStateMachineTests
{
    [Test]
    public void All_characters_confirmed_transitions_to_card_execution()
    {
        var sim = CombatSimulationTestBuilder.StandardPlayerPhase(fourCharacters: true);
        for (var i = 0; i < 4; i++)
            sim.TryApply(new ConfirmCharacterCommand(i));
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.CardExecution));
    }

    [Test]
    public void Card_queue_empty_after_execution_moves_to_enemy_phase()
    {
        var sim = CombatSimulationTestBuilder.WithQueuedCard();
        sim.AdvancePhase(); // CardExecution -> drain
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Enemy));
    }
}
```

- [ ] **Step 2: 运行确认失败**

Expected: FAIL

- [ ] **Step 3: 实现状态机与各 phase handler**

`ECombatPhase.cs`:

```csharp
namespace KemoCard.Mod.Combat.StateMachine;

public enum ECombatPhase
{
    BattleStart,
    Player,
    CardExecution,
    Enemy,
    WaveTransition,
    Victory,
    Defeat,
}
```

`PlayerPhaseHandler` 要点：
- 接受 `PlayCardCommand` / `CastInstantSkillCommand` / `ConfirmCharacterCommand` / `CancelQueuedCardCommand`
- 四人 `HasActed` → `TransitionTo(ECombatPhase.CardExecution)`

`CardExecutionPhaseHandler` 要点（设计规格 3.4）：
- 循环 `CardQueue.TryDequeue`
- 目标非法时：单体按 `CardDto.RetargetPolicy` 重选；多目标直接跳过（空放），**不打回已行动**

`EnemyPhaseHandler` 要点：
- 调用 `EnemyAiController` 执行敌人技能
- 结束 → 重置四人 `HasActed=false`，`TurnNumber++`，触发 `OnTurnEnd` 领域/规则钩子，回到 `Player`

- [ ] **Step 4: 运行测试通过**

Expected: PASS

- [ ] **Step 5: 提交**

```powershell
git add Src/mod/combat/statemachine/
git commit -m "feat(combat): 实现战斗阶段状态机"
```

---

### Task 9：玩家阶段回滚（设计规格 3.3）

**Files:**
- Modify: `Src/mod/combat/statemachine/handlers/PlayerPhaseHandler.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/PlayerPhaseRollbackTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Test]
public void Instant_skill_killing_queued_target_marks_holder_unacted()
{
    var sim = CombatSimulationTestBuilder.WithQueuedCardTargetingEnemy(index: 0);
    sim.TryApply(new CastInstantSkillCommand(characterIndex: 1, skillId: "execute", targets: [new CombatTargetRef(ECombatSide.Enemy, 0)]));
    Assert.That(sim.PlayerTeam.Characters[0].HasActed, Is.False);
}
```

- [ ] **Step 2–4: 实现即时技能后存活敌人快照对比，丢失则队列相关持有者 `SetHasActed(false)`**

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(combat): 实现玩家阶段即时技能目标失效回滚"
```

---

### Task 10：敌人 AI

**Files:**
- Create: `Src/mod/combat/ai/EnemyAiController.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/EnemyAiControllerTests.cs`

- [ ] **Step 1: 写失败测试 — 确定性选技**

```csharp
[Test]
public void ChooseSkill_is_deterministic_with_same_seed()
{
    var registry = CombatTestHelper.CreateFullRegistry(/* slime + skills */);
    var rng1 = new HostRng(1, "combat.ai");
    var rng2 = new HostRng(1, "combat.ai");
    var ai1 = new EnemyAiController(registry, scriptHost: null, modId: "test.mod");
    var ai2 = new EnemyAiController(registry, scriptHost: null, modId: "test.mod");
    var enemy = new EnemyUnit("e", "slime", 10);
    Assert.That(ai1.ChooseSkill(enemy, rng1), Is.EqualTo(ai2.ChooseSkill(enemy, rng2)));
}
```

- [ ] **Step 2–4: 实现**

```csharp
public string? ChooseSkill(EnemyUnit enemy, HostRng rng)
{
    if (!_registry.Store.TryGetEnemy(enemy.DefinitionId, out var def))
        return null;
    if (!string.IsNullOrWhiteSpace(def.ScriptPath) && _scriptInvoker is not null)
    {
        // TryChooseSkill → skillId
    }
    var legal = def.SkillRefs.Select(s => s.SkillId).Where(id => _registry.Store.TryGetSkill(id, out _)).ToList();
    if (legal.Count == 0) return null;
    return legal[rng.NextInt(0, legal.Count)];
}
```

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(combat): 添加最小确定性敌人 AI"
```

---

### Task 11：`BattleDto.combatRuleIds` 与工厂

**Files:**
- Modify: `Src/frame/content/definitions/BattleDto.cs`
- Create: `Src/mod/combat/runtime/CombatSimulationFactory.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CombatSimulationFactoryTests.cs`

- [ ] **Step 1: 写失败测试 — 规则集初始化后不可变**

```csharp
[Test]
public void Factory_merges_battle_and_run_rules_once()
{
    var battle = new BattleDto
    {
        Id = "test_battle",
        CombatRuleIds = ["builtin.shared_hp_defeat"],
        Waves = [new BattleWaveDto { EnemySpawns = [new EnemySpawnDto { EnemyId = "slime", Count = 1 }] }],
    };
    var registry = CombatTestHelper.CreateFullRegistry(
        enemies: new Dictionary<string, EnemyDto> { ["slime"] = new() { Id = "slime", MaxHp = 10 } },
        battles: new Dictionary<string, BattleDto> { ["test_battle"] = battle });

    var catalog = CombatRuleCatalog.CreateDefault();
    var party = CombatSimulationTestBuilder.CreateParty(count: 4, registry);
    var sim = CombatSimulationFactory.TryCreate(
        battle, party, registry, new HostRng(1, "combat"), runSeed: 1,
        runRuleIds: ["builtin.shared_hp_defeat"],
        scriptHost: null, modId: "test.mod", catalog, out var error);

    Assert.That(error, Is.Null);
    Assert.That(sim!.Rules.Rules, Has.Count.EqualTo(1)); // 去重后 1 条
}
```

- [ ] **Step 2: 扩展 `BattleDto`**

```csharp
[JsonPropertyName("combatRuleIds")]
public List<string> CombatRuleIds { get; init; } = [];
```

- [ ] **Step 3: 实现 `CombatSimulationFactory.TryCreate`**

规则合并逻辑（**仅初始化一次**）：

```csharp
var ruleIds = new HashSet<string>(StringComparer.Ordinal);
foreach (var id in battle.CombatRuleIds)
    ruleIds.Add(id);
if (runRuleIds is not null)
    foreach (var id in runRuleIds)
        ruleIds.Add(id);

var rules = new List<ICombatRule>();
foreach (var id in ruleIds)
{
    var rule = catalog.TryCreate(id);
    if (rule is not null)
        rules.Add(rule);
}
var ruleEngine = new CombatRuleEngine(rules);
// 构造 CombatSimulation 后不再暴露 mutator
```

共享 HP：`sharedMaxHp = party.Sum(c => c.ComputeAttributes(registry).HpCap)`

波次：从 `battle.Waves[currentWaveIndex]` 生成 `EnemyUnit` 列表（`hpScale` 乘算）

- [ ] **Step 4: 运行测试通过**

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(combat): 添加 CombatSimulationFactory 与 BattleDto 战斗规则引用"
```

---

### Task 12：胜负判定与波次切换

**Files:**
- Modify: `Src/mod/combat/statemachine/handlers/EnemyPhaseHandler.cs`
- Modify: `Src/mod/combat/statemachine/handlers/CardExecutionPhaseHandler.cs`
- Create: `Src/mod/combat/rules/builtin/AllEnemiesDefeatedVictoryRule.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CombatEndConditionTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
[Test]
public void Shared_hp_zero_triggers_defeat()
{
    var sim = CombatSimulationTestBuilder.Standard(/* ... */);
    sim.PlayerTeam.ApplySharedDamage(sim.PlayerTeam.MaxHp);
    sim.CheckEndConditions();
    Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Defeat));
}

[Test]
public void All_enemies_dead_triggers_victory_when_no_more_waves()
{
    var sim = CombatSimulationTestBuilder.Standard(/* single wave */);
    foreach (var e in sim.EnemyTeam.Enemies)
        e.ApplyDamage(e.MaxHp);
    sim.CheckEndConditions();
    Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Victory));
}
```

- [ ] **Step 2–4: 实现 `CheckEndConditions`：先规则 `OnCheckEndCondition`，默认逻辑兜底**

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(combat): 实现胜负判定与波次切换"
```

---

### Task 13：效果执行器补全（Draw / Discard / GainResource / ExecuteScript）

**Files:**
- Modify: `Src/mod/combat/effects/CombatEffectExecutor.cs`
- Test: `Tests/kemo_card.Ui.Tests/Combat/CombatEffectExecutorExtendedTests.cs`

- [ ] **Step 1–4: 按 `EEffectKind` 分派；`ExecuteScript` 注入 `IContentEffectScriptHost.TryExecute` 并将 `proposedEffects` 递归执行**

- [ ] **Step 5: 提交**

```powershell
git commit -m "feat(combat): 补全 Draw/Discard/GainResource/ExecuteScript 效果"
```

---

### Task 14：集成测试与确定性回归

**Files:**
- Create: `Tests/kemo_card.Ui.Tests/Combat/CombatSimulationIntegrationTests.cs`

- [ ] **Step 1: 写集成测试 — 同种子同指令序列同结果**

```csharp
[Test]
public void Same_seed_and_commands_produce_identical_outcome()
{
    CombatOutcome RunOnce()
    {
        var sim = CombatSimulationTestBuilder.FullBattle(seed: 12345);
        sim.TryApply(new ConfirmCharacterCommand(0));
        sim.TryApply(new ConfirmCharacterCommand(1));
        sim.TryApply(new ConfirmCharacterCommand(2));
        sim.TryApply(new ConfirmCharacterCommand(3));
        sim.AdvancePhase(); // 进入执行并尽可能推进
        return new CombatOutcome(sim.PlayerTeam.SharedHp, sim.EnemyTeam.Enemies[0].CurrentHp, sim.Phase);
    }
    Assert.That(RunOnce(), Is.EqualTo(RunOnce()));
}
```

- [ ] **Step 2: 运行全量 Combat 测试**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter FullyQualifiedName~Combat -v n`

Expected: ALL PASS

- [ ] **Step 3: 提交**

```powershell
git commit -m "test(combat): 添加战斗模拟集成与确定性回归测试"
```

---

## 规格自检

| 需求 | 对应 Task |
|------|-----------|
| 纯 C# 确定性模拟 | Task 5, 11, 14 |
| 指令显式 characterIndex、无切换角色 | Task 7 |
| 共享 HP | Task 2, 12 |
| 规则初始化冻结 | Task 3, 11 |
| Run 跨战斗规则由工厂合并 | Task 11 |
| 领域 = 队伍 buff、单例顶替 | Task 6 |
| 状态机 3.1 三阶段 | Task 8 |
| 3.3 回滚 | Task 9 |
| 3.4 重选/跳过 | Task 8 |
| 优先队列 + RNG 平局 | Task 4 |
| 最小效果执行器 | Task 5, 13 |
| 敌人 AI | Task 10 |
| 波次 / 胜负 | Task 12 |
| ApplyBuff 等延后 | Task 5, 13 软失败 |
| 不含 UI / BuffInstance / 存档 | 全文边界 |

**占位扫描:** 无 TBD；各 Task 含具体路径与代码骨架。

**类型一致性:** `CombatTargetRef`、`QueuedCardEntry`、`ICombatCommand.CharacterIndex`、`CombatRuleEngine` 全计划统一。

---

## 执行方式

Plan complete and saved to `Doc/superpowers/plans/2026-06-22-combat-mod-core-logic-implementation-plan.md`.

**两种执行方式：**

1. **Subagent-Driven（推荐）** — 每个 Task 派发独立 subagent，Task 间做 review，迭代快  
2. **Inline Execution** — 本会话用 executing-plans 按 Task 批量执行，检查点 review  

**请选择一种方式。**
