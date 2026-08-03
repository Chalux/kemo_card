# 条件判断系统 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地可扩展条件求值引擎（JSON AND/OR、双域注册表、Explain 叶子结果），并注册 Persistent 四件套 CondType；Combat 仅空表占位。

**Architecture:** `Src/frame/condition/` 提供表达式树、解析、求值与 `ConditionRegistry<TContext>`；`IPersistentCondContext` / `ICombatCondContext` 在 frame；具体 CondType 与启动注册在 `Src/mod/`。内容 DTO 暂无 `unlock` 字段，本计划**不**改 `ContentDefinitionValidator`，只交付可在加载期调用的 `ConditionParser.TryParse`。

**Tech Stack:** C# / Godot 4.6 Mono / System.Text.Json / NUnit（`Tests/kemo_card.Ui.Tests`）

**权威规格:** [2026-07-30-condition-system-design.md](../specs/2026-07-30-condition-system-design.md)

## Global Constraints

- `Src/frame/` 不得引用 `KemoCard.Mod.*`
- 面向用户文案用翻译键（`Resource/Locale/strings.csv`）；引擎不调用 `Localization.Tr` 拼最终句
- 不创建 `.uid`；提交说明用简体中文；仅在用户要求时 `git commit`
- 验证命令：

```powershell
$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"
dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~Condition" --nologo -v q
```

- 格式化仅针对本任务改过的文件：`dotnet format kemo_card.csproj --include <paths...>`

---

## File Structure

| 路径 | 职责 |
|------|------|
| `Src/frame/condition/ConditionProgress.cs` | `Current` / `Required` |
| `Src/frame/condition/ConditionRefs.cs` | UI 查表用 id 列表（`ItemIds` / `FlagIds`） |
| `Src/frame/condition/LeafEvalData.cs` | Checker 产出（passed/fill/progress/refs） |
| `Src/frame/condition/LeafResult.cs` | 叶子完整结果（含 tip keys） |
| `Src/frame/condition/ConditionEvalResult.cs` | `Passed` + `Leaves` |
| `Src/frame/condition/ConditionNode.cs` | `AndNode` / `OrNode` / `LeafNode` |
| `Src/frame/condition/ICondTypeHandler.cs` | 非泛型注册入口（Parse + Check） |
| `Src/frame/condition/CondTypeHandler.cs` | `CondTypeHandler<TContext, TArgs>` 适配到 `ICondTypeHandler` |
| `Src/frame/condition/ConditionRegistry.cs` | `ConditionRegistry<TContext>` |
| `Src/frame/condition/ConditionEvaluator.cs` | 全叶子求值 |
| `Src/frame/condition/ConditionParser.cs` | JSON → 表达式树 + 来源路径错误 |
| `Src/frame/condition/IPersistentCondContext.cs` | `HasFlag` / `GetItemCount` |
| `Src/frame/condition/ICombatCondContext.cs` | v1 空接口占位 |
| `Src/frame/condition/ConditionDomains.cs` | `Persistent` / `Combat` 静态注册表 |
| `Src/mod/global/Condition/BuiltinPersistentConditions.cs` | 注册四件套 |
| `Src/mod/combat/Condition/BuiltinCombatConditions.cs` | 空 `RegisterAll` |
| `Src/mod/ModFactory.cs` | Bootstrap 注册条件类型 |
| `Resource/Locale/strings.csv` | 短/长提示键 |
| `Tests/kemo_card.Ui.Tests/Condition/*.cs` | 解析 / 求值 / Persistent 类型测试 |
| `Tests/kemo_card.Ui.Tests/Condition/FakePersistentCondContext.cs` | 测试用 Context |

---

### Task 1: 结果类型与表达式节点

**Files:**
- Create: `Src/frame/condition/ConditionProgress.cs`
- Create: `Src/frame/condition/ConditionRefs.cs`
- Create: `Src/frame/condition/LeafEvalData.cs`
- Create: `Src/frame/condition/LeafResult.cs`
- Create: `Src/frame/condition/ConditionEvalResult.cs`
- Create: `Src/frame/condition/ConditionNode.cs`
- Test: `Tests/kemo_card.Ui.Tests/Condition/ConditionResultShapeTests.cs`

**Interfaces:**
- Produces: 下列类型，供后续 Parser/Evaluator/Handler 使用

- [ ] **Step 1: 写入结果与节点类型**

```csharp
// Src/frame/condition/ConditionProgress.cs
namespace KemoCard.Frame.Condition;

public readonly record struct ConditionProgress(int Current, int Required);

// Src/frame/condition/ConditionRefs.cs
namespace KemoCard.Frame.Condition;

public sealed class ConditionRefs
{
    public IReadOnlyList<string> ItemIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FlagIds { get; init; } = Array.Empty<string>();
}

// Src/frame/condition/LeafEvalData.cs
namespace KemoCard.Frame.Condition;

public sealed class LeafEvalData
{
    public required bool Passed { get; init; }
    public IReadOnlyList<object?> Fill { get; init; } = Array.Empty<object?>();
    public ConditionProgress? Progress { get; init; }
    public ConditionRefs? Refs { get; init; }
}

// Src/frame/condition/LeafResult.cs
namespace KemoCard.Frame.Condition;

public sealed class LeafResult
{
    public required string CondType { get; init; }
    public required bool Passed { get; init; }
    public required string ShortTipKey { get; init; }
    public required string LongTipKey { get; init; }
    public IReadOnlyList<object?> Fill { get; init; } = Array.Empty<object?>();
    public ConditionProgress? Progress { get; init; }
    public ConditionRefs? Refs { get; init; }
}

// Src/frame/condition/ConditionEvalResult.cs
namespace KemoCard.Frame.Condition;

public sealed class ConditionEvalResult
{
    public required bool Passed { get; init; }
    public required IReadOnlyList<LeafResult> Leaves { get; init; }
}

// Src/frame/condition/ConditionNode.cs
namespace KemoCard.Frame.Condition;

public abstract record ConditionNode;

public sealed record AndNode(IReadOnlyList<ConditionNode> Children) : ConditionNode;

public sealed record OrNode(IReadOnlyList<ConditionNode> Children) : ConditionNode;

public sealed record LeafNode(string CondType, object Args) : ConditionNode;
```

- [ ] **Step 2: 写形状冒烟测试**

```csharp
using KemoCard.Frame.Condition;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Condition;

[TestFixture]
public sealed class ConditionResultShapeTests
{
    [Test]
    public void LeafResult_holds_optional_progress_and_refs()
    {
        var leaf = new LeafResult
        {
            CondType = "HasFlag",
            Passed = true,
            ShortTipKey = "COND_HAS_FLAG_SHORT",
            LongTipKey = "COND_HAS_FLAG_LONG",
            Fill = ["intro"],
            Progress = new ConditionProgress(1, 1),
            Refs = new ConditionRefs { FlagIds = ["intro"] },
        };

        Assert.That(leaf.Progress!.Value.Current, Is.EqualTo(1));
        Assert.That(leaf.Refs!.FlagIds[0], Is.EqualTo("intro"));
    }
}
```

- [ ] **Step 3: 跑测试确认通过**

```powershell
$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"
dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~ConditionResultShapeTests" --nologo -v q
```

Expected: PASS

- [ ] **Step 4:（可选）提交** — 仅当用户要求时：`条件系统：新增求值结果与表达式节点类型`

---

### Task 2: Registry + Handler 适配器

**Files:**
- Create: `Src/frame/condition/ICondTypeHandler.cs`
- Create: `Src/frame/condition/CondTypeHandler.cs`
- Create: `Src/frame/condition/ConditionRegistry.cs`
- Create: `Src/frame/condition/ConditionDomains.cs`
- Create: `Src/frame/condition/IPersistentCondContext.cs`
- Create: `Src/frame/condition/ICombatCondContext.cs`
- Test: `Tests/kemo_card.Ui.Tests/Condition/ConditionRegistryTests.cs`

**Interfaces:**
- Consumes: Task 1 类型
- Produces:
  - `ConditionRegistry<TContext>.Register(ICondTypeHandler<TContext>)`
  - `TryGet(string id, out ICondTypeHandler<TContext>? handler)`
  - `Contains(string id)`
  - `Clear()`
  - `ConditionDomains.Persistent` / `ConditionDomains.Combat`

- [ ] **Step 1: 写失败测试（未注册则 Contains 为 false；Register 后可 Get）**

```csharp
using KemoCard.Frame.Condition;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Condition;

[TestFixture]
public sealed class ConditionRegistryTests
{
    private sealed class DummyCtx;

    [Test]
    public void Register_then_TryGet_returns_handler()
    {
        var registry = new ConditionRegistry<DummyCtx>();
        var handler = CondTypeHandler.Create<DummyCtx, string>(
            id: "Ping",
            shortTipKey: "S",
            longTipKey: "L",
            tryParse: (args, path, out parsed, out error) =>
            {
                parsed = "x";
                error = null;
                return true;
            },
            check: (parsed, _) => new LeafEvalData { Passed = true, Fill = [parsed] });

        registry.Register(handler);

        Assert.That(registry.Contains("Ping"), Is.True);
        Assert.That(registry.TryGet("Ping", out var found), Is.True);
        Assert.That(found!.Id, Is.EqualTo("Ping"));
    }

    [Test]
    public void Register_duplicate_id_throws()
    {
        var registry = new ConditionRegistry<DummyCtx>();
        var a = CondTypeHandler.Create<DummyCtx, string>(
            "Ping", "S", "L",
            (System.Text.Json.JsonElement _, string _, out string? p, out string? e) => { p = "a"; e = null; return true; },
            (_, _) => new LeafEvalData { Passed = true });
        var b = CondTypeHandler.Create<DummyCtx, string>(
            "Ping", "S", "L",
            (System.Text.Json.JsonElement _, string _, out string? p, out string? e) => { p = "b"; e = null; return true; },
            (_, _) => new LeafEvalData { Passed = true });

        registry.Register(a);
        Assert.Throws<InvalidOperationException>(() => registry.Register(b));
    }
}
```

- [ ] **Step 2: 跑测试确认失败（类型不存在）**

Expected: FAIL（缺少 `ConditionRegistry` 等）

- [ ] **Step 3: 实现 Handler 与 Registry**

```csharp
// Src/frame/condition/IPersistentCondContext.cs
namespace KemoCard.Frame.Condition;

public interface IPersistentCondContext
{
    bool HasFlag(string flagId);
    int GetItemCount(string itemId);
}

// Src/frame/condition/ICombatCondContext.cs
namespace KemoCard.Frame.Condition;

/// <summary>Combat 域上下文占位；v1 无业务查询方法。</summary>
public interface ICombatCondContext
{
}

// Src/frame/condition/ICondTypeHandler.cs
using System.Text.Json;

namespace KemoCard.Frame.Condition;

public interface ICondTypeHandler<TContext>
{
    string Id { get; }
    string ShortTipKey { get; }
    string LongTipKey { get; }
    bool TryParse(JsonElement args, string sourcePath, out object? parsedArgs, out string? error);
    LeafEvalData Check(object parsedArgs, TContext context);
}

// Src/frame/condition/CondTypeHandler.cs
using System.Text.Json;

namespace KemoCard.Frame.Condition;

public static class CondTypeHandler
{
    public delegate bool TryParseDelegate<TArgs>(
        JsonElement args,
        string sourcePath,
        out TArgs? parsed,
        out string? error);

    public static ICondTypeHandler<TContext> Create<TContext, TArgs>(
        string id,
        string shortTipKey,
        string longTipKey,
        TryParseDelegate<TArgs> tryParse,
        Func<TArgs, TContext, LeafEvalData> check)
    {
        return new Handler<TContext, TArgs>(id, shortTipKey, longTipKey, tryParse, check);
    }

    private sealed class Handler<TContext, TArgs> : ICondTypeHandler<TContext>
    {
        private readonly TryParseDelegate<TArgs> _tryParse;
        private readonly Func<TArgs, TContext, LeafEvalData> _check;

        public Handler(
            string id,
            string shortTipKey,
            string longTipKey,
            TryParseDelegate<TArgs> tryParse,
            Func<TArgs, TContext, LeafEvalData> check)
        {
            Id = id;
            ShortTipKey = shortTipKey;
            LongTipKey = longTipKey;
            _tryParse = tryParse;
            _check = check;
        }

        public string Id { get; }
        public string ShortTipKey { get; }
        public string LongTipKey { get; }

        public bool TryParse(JsonElement args, string sourcePath, out object? parsedArgs, out string? error)
        {
            if (!_tryParse(args, sourcePath, out var typed, out error))
            {
                parsedArgs = null;
                return false;
            }

            parsedArgs = typed;
            return true;
        }

        public LeafEvalData Check(object parsedArgs, TContext context)
        {
            return _check((TArgs)parsedArgs, context);
        }
    }
}

// Src/frame/condition/ConditionRegistry.cs
namespace KemoCard.Frame.Condition;

public sealed class ConditionRegistry<TContext>
{
    private readonly Dictionary<string, ICondTypeHandler<TContext>> _handlers = new(StringComparer.Ordinal);

    public void Register(ICondTypeHandler<TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (string.IsNullOrWhiteSpace(handler.Id))
        {
            throw new ArgumentException("CondType id 不能为空。", nameof(handler));
        }

        if (!_handlers.TryAdd(handler.Id, handler))
        {
            throw new InvalidOperationException($"CondType '{handler.Id}' 已注册。");
        }
    }

    public bool Contains(string id) =>
        !string.IsNullOrWhiteSpace(id) && _handlers.ContainsKey(id);

    public bool TryGet(string id, out ICondTypeHandler<TContext>? handler)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            handler = null;
            return false;
        }

        if (_handlers.TryGetValue(id, out var found))
        {
            handler = found;
            return true;
        }

        handler = null;
        return false;
    }

    public void Clear() => _handlers.Clear();
}

// Src/frame/condition/ConditionDomains.cs
namespace KemoCard.Frame.Condition;

public static class ConditionDomains
{
    public static ConditionRegistry<IPersistentCondContext> Persistent { get; } = new();
    public static ConditionRegistry<ICombatCondContext> Combat { get; } = new();
}
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~ConditionRegistryTests" --nologo -v q
```

Expected: PASS

- [ ] **Step 5:（可选）提交** — `条件系统：新增 CondType 注册表与双域入口`

---

### Task 3: Evaluator（全叶子、AND/OR 空节点语义）

**Files:**
- Create: `Src/frame/condition/ConditionEvaluator.cs`
- Test: `Tests/kemo_card.Ui.Tests/Condition/ConditionEvaluatorTests.cs`

**Interfaces:**
- Consumes: `ConditionNode`, `ConditionRegistry<TContext>`, `ICondTypeHandler<TContext>`
- Produces: `ConditionEvaluator.Evaluate(ConditionNode root, TContext context, ConditionRegistry<TContext> registry) -> ConditionEvalResult`

- [ ] **Step 1: 写失败测试**

用 Task 2 的 `CondTypeHandler.Create` 注册两个假类型 `Always` / `Never`（参数忽略），覆盖：

1. 空 `AndNode([])` → `Passed == true`，`Leaves` 空  
2. 空 `OrNode([])` → `Passed == false`  
3. AND 两叶一真一假 → `Passed == false`，`Leaves.Count == 2`  
4. OR 两叶一真一假 → `Passed == true`，仍返回 2 片叶子  

```csharp
using KemoCard.Frame.Condition;
using NUnit.Framework;
using System.Text.Json;

namespace KemoCard.Ui.Tests.Condition;

[TestFixture]
public sealed class ConditionEvaluatorTests
{
    private sealed class Ctx;

    private static ConditionRegistry<Ctx> CreateRegistry()
    {
        var registry = new ConditionRegistry<Ctx>();
        registry.Register(CondTypeHandler.Create<Ctx, object?>(
            "Always", "S", "L",
            (JsonElement _, string _, out object? p, out string? e) => { p = null; e = null; return true; },
            (_, _) => new LeafEvalData { Passed = true }));
        registry.Register(CondTypeHandler.Create<Ctx, object?>(
            "Never", "S", "L",
            (JsonElement _, string _, out object? p, out string? e) => { p = null; e = null; return true; },
            (_, _) => new LeafEvalData { Passed = false }));
        return registry;
    }

    [Test]
    public void Empty_and_is_true()
    {
        var result = ConditionEvaluator.Evaluate(new AndNode([]), new Ctx(), CreateRegistry());
        Assert.That(result.Passed, Is.True);
        Assert.That(result.Leaves, Is.Empty);
    }

    [Test]
    public void Empty_or_is_false()
    {
        var result = ConditionEvaluator.Evaluate(new OrNode([]), new Ctx(), CreateRegistry());
        Assert.That(result.Passed, Is.False);
    }

    [Test]
    public void And_evaluates_all_leaves()
    {
        var root = new AndNode([
            new LeafNode("Always", null!),
            new LeafNode("Never", null!),
        ]);
        var result = ConditionEvaluator.Evaluate(root, new Ctx(), CreateRegistry());
        Assert.That(result.Passed, Is.False);
        Assert.That(result.Leaves, Has.Count.EqualTo(2));
    }

    [Test]
    public void Or_evaluates_all_leaves_even_when_passed()
    {
        var root = new OrNode([
            new LeafNode("Always", null!),
            new LeafNode("Never", null!),
        ]);
        var result = ConditionEvaluator.Evaluate(root, new Ctx(), CreateRegistry());
        Assert.That(result.Passed, Is.True);
        Assert.That(result.Leaves, Has.Count.EqualTo(2));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Expected: FAIL（缺少 `ConditionEvaluator`）

- [ ] **Step 3: 实现 Evaluator**

```csharp
namespace KemoCard.Frame.Condition;

public static class ConditionEvaluator
{
    public static ConditionEvalResult Evaluate<TContext>(
        ConditionNode root,
        TContext context,
        ConditionRegistry<TContext> registry)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(registry);

        var leaves = new List<LeafResult>();
        var passed = EvalNode(root, context, registry, leaves);
        return new ConditionEvalResult { Passed = passed, Leaves = leaves };
    }

    private static bool EvalNode<TContext>(
        ConditionNode node,
        TContext context,
        ConditionRegistry<TContext> registry,
        List<LeafResult> leaves)
    {
        switch (node)
        {
            case AndNode and:
            {
                var ok = true;
                foreach (var child in and.Children)
                {
                    if (!EvalNode(child, context, registry, leaves))
                    {
                        ok = false;
                    }
                }

                return ok;
            }
            case OrNode or:
            {
                var ok = false;
                foreach (var child in or.Children)
                {
                    if (EvalNode(child, context, registry, leaves))
                    {
                        ok = true;
                    }
                }

                return ok;
            }
            case LeafNode leaf:
            {
                if (!registry.TryGet(leaf.CondType, out var handler) || handler is null)
                {
                    throw new InvalidOperationException($"未注册的 CondType: {leaf.CondType}");
                }

                var data = handler.Check(leaf.Args, context);
                leaves.Add(new LeafResult
                {
                    CondType = handler.Id,
                    Passed = data.Passed,
                    ShortTipKey = handler.ShortTipKey,
                    LongTipKey = handler.LongTipKey,
                    Fill = data.Fill,
                    Progress = data.Progress,
                    Refs = data.Refs,
                });
                return data.Passed;
            }
            default:
                throw new InvalidOperationException($"未知条件节点: {node.GetType().Name}");
        }
    }
}
```

注意：`LeafNode` 的 `Args` 在测试里可为 `null!`；正式 Parse 后 Always/Never 的 `TArgs` 若为 `object?`，Check 内接受 null。若运行时 `(TArgs)parsedArgs` 对 `null` + 值类型会炸——假类型用 `object?` 即可。

- [ ] **Step 4: 跑测试确认通过**

Expected: PASS

- [ ] **Step 5:（可选）提交** — `条件系统：实现 AND/OR 全叶子求值器`

---

### Task 4: ConditionParser（JSON 语法 + 来源路径）

**Files:**
- Create: `Src/frame/condition/ConditionParser.cs`
- Test: `Tests/kemo_card.Ui.Tests/Condition/ConditionParserTests.cs`

**Interfaces:**
- Consumes: `ConditionRegistry<TContext>`、`ICondTypeHandler.TryParse`
- Produces:

```csharp
public static bool TryParse<TContext>(
    JsonElement element,
    ConditionRegistry<TContext> registry,
    string sourcePath,
    out ConditionNode? expression,
    out string? error);
```

规则（必须全部覆盖测试）：

| 输入 | 结果 |
|------|------|
| `{}` | `AndNode([])` 成功 |
| `[]` | `OrNode([])` 成功 |
| 对象成员 | 每个 key 必须 `registry.Contains`；`$or` 等失败，error 含 `sourcePath` |
| 对象 value | 交给 handler.TryParse；失败 error 含 path（建议 `sourcePath.HasFlag`） |
| 数组元素 | 对象或数组，递归；其它 ValueKind 失败 |
| 对象 value 为「像表达式的对象」 | **仍当参数**交给 TryParse（由类型决定成败），不当子树 |

- [ ] **Step 1: 写 Parser 测试（先挂假 CondType `Tag`，参数必须是单字符串数组）**

```csharp
using System.Text.Json;
using KemoCard.Frame.Condition;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Condition;

[TestFixture]
public sealed class ConditionParserTests
{
    private sealed class Ctx;

    private static ConditionRegistry<Ctx> Registry()
    {
        var r = new ConditionRegistry<Ctx>();
        r.Register(CondTypeHandler.Create<Ctx, string>(
            "Tag", "S", "L",
            (JsonElement args, string path, out string? parsed, out string? error) =>
            {
                if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() != 1
                    || args[0].ValueKind != JsonValueKind.String)
                {
                    parsed = null;
                    error = $"{path}: Tag 参数须为单字符串数组";
                    return false;
                }

                parsed = args[0].GetString();
                error = null;
                return true;
            },
            (tag, _) => new LeafEvalData { Passed = true, Fill = [tag] }));
        return r;
    }

    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Test]
    public void Parse_empty_object_and_array()
    {
        Assert.That(ConditionParser.TryParse(El("{}"), Registry(), "c.json:unlock", out var and, out _), Is.True);
        Assert.That(and, Is.TypeOf<AndNode>());
        Assert.That(((AndNode)and!).Children, Is.Empty);

        Assert.That(ConditionParser.TryParse(El("[]"), Registry(), "c.json:unlock", out var or, out _), Is.True);
        Assert.That(or, Is.TypeOf<OrNode>());
    }

    [Test]
    public void Parse_unknown_type_includes_source_path()
    {
        var ok = ConditionParser.TryParse(El("""{"$or":[]}"""), Registry(), "mod/a.json:unlock", out _, out var error);
        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("mod/a.json:unlock"));
        Assert.That(error, Does.Contain("$or"));
    }

    [Test]
    public void Parse_bad_args_includes_path()
    {
        var ok = ConditionParser.TryParse(El("""{"Tag":[]}"""), Registry(), "x.json:cond", out _, out var error);
        Assert.That(ok, Is.False);
        Assert.That(error, Does.Contain("x.json:cond"));
    }

    [Test]
    public void Parse_or_of_and_leaves()
    {
        var json = """[{ "Tag": ["a"] }, { "Tag": ["b"] }]""";
        Assert.That(ConditionParser.TryParse(El(json), Registry(), "p", out var expr, out _), Is.True);
        Assert.That(expr, Is.TypeOf<OrNode>());
        Assert.That(((OrNode)expr!).Children, Has.Count.EqualTo(2));
    }
}
```

- [ ] **Step 2: 跑测试确认失败** → 实现 Parser：

```csharp
using System.Text.Json;

namespace KemoCard.Frame.Condition;

public static class ConditionParser
{
    public static bool TryParse<TContext>(
        JsonElement element,
        ConditionRegistry<TContext> registry,
        string sourcePath,
        out ConditionNode? expression,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(registry);
        sourcePath ??= "";

        return TryParseNode(element, registry, sourcePath, out expression, out error);
    }

    private static bool TryParseNode<TContext>(
        JsonElement element,
        ConditionRegistry<TContext> registry,
        string path,
        out ConditionNode? expression,
        out string? error)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return TryParseAndObject(element, registry, path, out expression, out error);
            case JsonValueKind.Array:
                return TryParseOrArray(element, registry, path, out expression, out error);
            default:
                expression = null;
                error = $"{path}: 条件表达式须为对象或数组，实际为 {element.ValueKind}";
                return false;
        }
    }

    private static bool TryParseAndObject<TContext>(
        JsonElement obj,
        ConditionRegistry<TContext> registry,
        string path,
        out ConditionNode? expression,
        out string? error)
    {
        var children = new List<ConditionNode>();
        foreach (var prop in obj.EnumerateObject())
        {
            var typeId = prop.Name;
            var leafPath = $"{path}.{typeId}";
            if (!registry.TryGet(typeId, out var handler) || handler is null)
            {
                expression = null;
                error = $"{leafPath}: 未知 CondType '{typeId}'";
                return false;
            }

            if (!handler.TryParse(prop.Value, leafPath, out var args, out var parseError))
            {
                expression = null;
                error = parseError ?? $"{leafPath}: 参数解析失败";
                return false;
            }

            children.Add(new LeafNode(typeId, args!));
        }

        expression = new AndNode(children);
        error = null;
        return true;
    }

    private static bool TryParseOrArray<TContext>(
        JsonElement arr,
        ConditionRegistry<TContext> registry,
        string path,
        out ConditionNode? expression,
        out string? error)
    {
        var children = new List<ConditionNode>();
        var index = 0;
        foreach (var item in arr.EnumerateArray())
        {
            var childPath = $"{path}[{index}]";
            if (!TryParseNode(item, registry, childPath, out var child, out error))
            {
                expression = null;
                return false;
            }

            children.Add(child!);
            index++;
        }

        expression = new OrNode(children);
        error = null;
        return true;
    }
}
```

说明：System.Text.Json 对重复 key 通常保留后者，无法可靠检测重复 CondType；规格允许依赖 JSON key 唯一。不必额外做 Utf8 重复扫描（YAGNI）。

- [ ] **Step 3: 跑测试确认通过**

- [ ] **Step 4:（可选）提交** — `条件系统：实现条件 JSON 解析与来源路径错误`

---

### Task 5: Persistent 四件套 CondType

**Files:**
- Create: `Src/mod/global/Condition/BuiltinPersistentConditions.cs`
- Create: `Tests/kemo_card.Ui.Tests/Condition/FakePersistentCondContext.cs`
- Create: `Tests/kemo_card.Ui.Tests/Condition/PersistentCondTypeTests.cs`
- Modify: `Resource/Locale/strings.csv`（追加键，见下）

**Interfaces:**
- Consumes: `IPersistentCondContext`、`CondTypeHandler`、`ConditionDomains.Persistent`
- Produces: `BuiltinPersistentConditions.RegisterAll(ConditionRegistry<IPersistentCondContext>)`

翻译键（keys 列追加到 csv，中英列按项目现有格式）：

| key | zh | en |
|-----|----|----|
| `COND_HAS_FLAG_SHORT` | 条件未满足 | Requirement not met |
| `COND_HAS_FLAG_LONG` | 需要旗标 | Requires flag |
| `COND_NOT_HAS_FLAG_SHORT` | 条件未满足 | Requirement not met |
| `COND_NOT_HAS_FLAG_LONG` | 不能拥有旗标 | Must not have flag |
| `COND_HAS_ALL_ITEMS_SHORT` | 物品不足 | Not enough items |
| `COND_HAS_ALL_ITEMS_LONG` | 需要全部物品 | Requires all items |
| `COND_HAS_ANY_ITEM_SHORT` | 物品不足 | Not enough items |
| `COND_HAS_ANY_ITEM_LONG` | 需要任一物品 | Requires any item |

（句式保持模板级；具体物品名由 UI 用 `Refs` 查表后自行拼接，引擎不 Tr。）

- [ ] **Step 1: Fake context + 失败测试**

```csharp
// FakePersistentCondContext.cs
using KemoCard.Frame.Condition;

namespace KemoCard.Ui.Tests.Condition;

public sealed class FakePersistentCondContext : IPersistentCondContext
{
    public HashSet<string> Flags { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Items { get; } = new(StringComparer.Ordinal);

    public bool HasFlag(string flagId) => Flags.Contains(flagId);
    public int GetItemCount(string itemId) =>
        Items.TryGetValue(itemId, out var n) ? n : 0;
}
```

测试要点：

1. `HasFlag` / `NotHasFlag` 参数 `["x"]`；空数组 Parse 失败且含 path  
2. `HasAllItems`：`[["wood",5],["stone",3]]`，缺一则 `Passed=false`；`Refs.ItemIds` 含两者；`Progress` 建议 `Current=已满足条目数`，`Required=条目总数`（All）或对 Any 用 `Current=0/1`、`Required=1`  
3. `HasAnyItem`：有一足够则通过  
4. 组合 JSON：`{"HasFlag":["intro"],"HasAllItems":[["wood",1]]}` 经 Parse+Evaluate

进度约定（写死在实现与测试中，避免歧义）：

- `HasFlag` / `NotHasFlag`：`Progress = (HasFlag?1:0, 1)`，`Refs.FlagIds = [flagId]`，`Fill = [flagId]`  
- `HasAllItems`：对每个 `[id,need]`，`have = GetItemCount(id)`；全部 `have >= need` 才通过；`Progress = (满足条数, 总条数)`；`Refs.ItemIds` = 全部 id；`Fill` 可为空数组（UI 靠 refs）  
- `HasAnyItem`：任一满足则通过；`Progress = (任一条满足?1:0, 1)`；`Refs.ItemIds` = 全部 id  

参数解析辅助：要求根为 Array；All/Any 的每个元素为长度 2 的数组 `[string, number]`（number 为整数且 `>= 1`）。

- [ ] **Step 2: 实现 `BuiltinPersistentConditions.RegisterAll`**

结构示例（完整实现按上面约定展开四个 Register）：

```csharp
using System.Text.Json;
using KemoCard.Frame.Condition;

namespace KemoCard.Mod.Global.Condition;

public static class BuiltinPersistentConditions
{
    public static void RegisterAll(ConditionRegistry<IPersistentCondContext> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(CondTypeHandler.Create<IPersistentCondContext, string>(
            "HasFlag",
            "COND_HAS_FLAG_SHORT",
            "COND_HAS_FLAG_LONG",
            TryParseSingleString,
            static (flagId, ctx) =>
            {
                var ok = ctx.HasFlag(flagId);
                return new LeafEvalData
                {
                    Passed = ok,
                    Fill = [flagId],
                    Progress = new ConditionProgress(ok ? 1 : 0, 1),
                    Refs = new ConditionRefs { FlagIds = [flagId] },
                };
            }));
        // NotHasFlag / HasAllItems / HasAnyItem 同文件内写完
    }

    private static bool TryParseSingleString(
        JsonElement args, string path, out string? parsed, out string? error) { /* ... */ }

    private static bool TryParseItemList(
        JsonElement args, string path, out List<(string Id, int Count)>? parsed, out string? error) { /* ... */ }
}
```

- [ ] **Step 3: 追加 `strings.csv` 八行键**

- [ ] **Step 4: 跑 Persistent 测试通过**

- [ ] **Step 5:（可选）提交** — `条件系统：注册 Persistent 四件套 CondType`

---

### Task 6: Combat 空注册 + ModFactory 启动挂钩

**Files:**
- Create: `Src/mod/combat/Condition/BuiltinCombatConditions.cs`
- Modify: `Src/mod/ModFactory.cs`（在 `RegisterBuiltinKeywords` 旁增加 `RegisterBuiltinConditions`）
- Test: `Tests/kemo_card.Ui.Tests/Condition/ConditionDomainsBootstrapTests.cs`

**Interfaces:**
- Consumes: `ConditionDomains`、`BuiltinPersistentConditions`、`BuiltinCombatConditions`
- Produces: Bootstrap 后 `ConditionDomains.Persistent` 含 4 个 id；`Combat` 仍 `Clear` 后为空

```csharp
// BuiltinCombatConditions.cs
using KemoCard.Frame.Condition;

namespace KemoCard.Mod.Combat.Condition;

public static class BuiltinCombatConditions
{
    public static void RegisterAll(ConditionRegistry<ICombatCondContext> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        // v1：有意不注册业务 CondType
    }
}
```

`ModFactory.Bootstrap` 内：

```csharp
RegisterBuiltinKeywords();
RegisterBuiltinConditions();
```

```csharp
private static void RegisterBuiltinConditions()
{
    ConditionDomains.Persistent.Clear();
    ConditionDomains.Combat.Clear();
    BuiltinPersistentConditions.RegisterAll(ConditionDomains.Persistent);
    BuiltinCombatConditions.RegisterAll(ConditionDomains.Combat);
}
```

测试（不依赖完整 Bootstrap）：直接调用两个 `RegisterAll` 到新 registry 或 Clear 后的 Domains，断言 `HasFlag`/`NotHasFlag`/`HasAllItems`/`HasAnyItem` 均 `Contains`，且 Combat 对任意 id `Contains` 为 false。

注意：单测若共用 `ConditionDomains.Persistent` 静态实例，**每个测试** `Clear` + `RegisterAll`，避免顺序污染；或只测 `BuiltinPersistentConditions.RegisterAll(new ConditionRegistry<...>())`。

- [ ] **Step 1–4: 测试 → 实现 → 通过 →（可选）提交** — `条件系统：Mod 启动注册 Persistent/Combat 域表`

---

### Task 7: 解析+求值集成测试与文档勾选

**Files:**
- Create: `Tests/kemo_card.Ui.Tests/Condition/ConditionIntegrationTests.cs`
- Modify: `Doc/superpowers/specs/2026-07-30-condition-system-design.md` — 状态改为「实现中」或「已实现（引擎+Persistent）」取决于本任务结束时是否全部完成  
- Modify: `Doc/INDEX.md` — 「进行中计划」指向本 plan（若尚未）

- [ ] **Step 1: 集成测试**

```csharp
// 使用 BuiltinPersistentConditions + FakePersistentCondContext
// JSON: {"HasFlag":["intro"],"HasAllItems":[["wood",2],["stone",1]]}
// Flags={intro}, Items={wood:2,stone:0} → Passed=false, Leaves.Count==2
// 补 stone:1 → Passed=true
// JSON: [] → Passed=false
// JSON: {} → Passed=true
```

- [ ] **Step 2: 全量 Condition 过滤测试**

```powershell
$env:DOTNET_CLI_UI_LANGUAGE="en"; $env:VSLANG="1033"
dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~Condition" --nologo -v q
```

Expected: 全部 PASS

- [ ] **Step 3: 更新规格文首状态为「引擎与 Persistent 四件套已实现；Combat CondType / 内容 DTO 字段未接」**

- [ ] **Step 4:（可选）提交** — `条件系统：完成集成测试并更新规格状态`

---

## Spec Coverage（自检）

| 规格条款 | 任务 |
|----------|------|
| 共享引擎 + 双域注册表 | T2, T6 |
| `{}` AND / `[]` OR，value=参数 | T4 |
| 空 `{}` 真 / 空 `[]` 假 | T3, T4, T7 |
| 未知类型 / `$or` 加载失败 + 路径 | T4 |
| 结构化 Leaves，无默认聚合，全叶子 | T3 |
| tip 模板键 + fill/progress/refs | T1, T5 |
| Context 接口 | T2, T5 |
| 只 Check 不 Cost | 全程无 Apply |
| Persistent 四件套 + NotHasFlag | T5 |
| Combat 空表 | T6 |
| 纯拉取 | 无订阅 API |
| 内容 DTO unlock / Validator | **有意不做**（规格 §8 字段未定；Parser API 已就绪） |
| 脚本注册 / 具名包 / `$or` 糖 | 非范围 |

## Placeholder 扫描

无 TBD；进度语义在 T5 写死；重复 JSON key 检测明确 YAGNI。

## Type 一致性

- `ConditionParser.TryParse` / `ConditionEvaluator.Evaluate` / `ConditionRegistry<TContext>` 全程同 `TContext`
- Domains：`Persistent` → `IPersistentCondContext`；`Combat` → `ICombatCondContext`
- Handler 工厂：`CondTypeHandler.Create<TContext, TArgs>`

---

## 执行方式

Plan 已保存到 `Doc/superpowers/plans/2026-07-30-condition-system-implementation-plan.md`。

**1. Subagent-Driven（推荐）** — 每任务新开 subagent，任务间审查  

**2. Inline Execution** — 本会话按 executing-plans 连续做  

要哪种？
