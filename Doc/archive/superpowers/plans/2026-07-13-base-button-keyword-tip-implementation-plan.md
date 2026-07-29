# BaseButton 与词条提示 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现可动态注册的词条 Catalog、可复用 KeywordTipService，以及带可配置动画的 BaseButton；BaseCardItem 预留同一套 Tip 调用入口。

**Architecture:** `KeywordCatalog`（frame）管词条注册与 `{name}` 参数替换；`KeywordTipService`（mod）管堆叠显示与左右贴边翻转；`BaseButton` / `BaseCardItem` 只委托 Show/Hide。Tip 挂在 MainRoot 顶层 CanvasLayer，通过静态 Current 访问。

**Tech Stack:** Godot 4.6 Mono / C# / NUnit

**Spec:** `Doc/superpowers/specs/2026-07-13-base-button-keyword-tip-design.md`

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `Src/frame/content/keywords/TipSide.cs` | Left / Right 枚举 |
| `Src/frame/content/keywords/KeywordEntry.cs` | 词条定义 |
| `Src/frame/content/keywords/KeywordTipRequest.cs` | 展示请求（id + 命名参数） |
| `Src/frame/content/keywords/KeywordTextFormatter.cs` | `{name}` 替换（缺参保留原文） |
| `Src/frame/content/keywords/KeywordCatalog.cs` | 注册表：后写覆盖 + Warning |
| `Src/mod/global/Ui/Tip/KeywordTipPanel.cs` + `.tscn` | 单条 tip UI |
| `Src/mod/global/Ui/Tip/KeywordTipService.cs` + TipLayer 场景/节点 | Show/Hide、定位、堆叠 |
| `Src/mod/global/Ui/Comp/BaseButton.cs` | 动画 + hover/focus 触发 tip |
| `Src/mod/global/Ui/Comp/BaseCardItem.cs` | ShowTips / HideTips |
| `Src/mod/global/Def/BuiltinKeywords.cs` | 内置词条注册 |
| `Resource/Locale/strings.csv` | 词条本地化键 |
| `Src/MainRoot.cs` / `.tscn` | 挂 TipLayer、启动时注册内置词条 |
| `Tests/.../KeywordCatalogTests.cs` | Catalog + Formatter 单测 |
| `Tests/.../KeywordTextFormatterTests.cs` | 参数替换单测（可合并） |

---

### Task 1: Keyword 数据模型 + Catalog + Formatter（TDD）

**Files:**
- Create: `Src/frame/content/keywords/*.cs`
- Create: `Tests/kemo_card.Ui.Tests/KeywordCatalogTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Frame.Content.Keywords;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class KeywordCatalogTests
{
	[Test]
	public void Register_then_TryGet_returns_entry()
	{
		var catalog = new KeywordCatalog();
		catalog.Register(new KeywordEntry("exhaust", "KW_EXHAUST_TITLE", "KW_EXHAUST_DESC"));
		Assert.That(catalog.TryGet("exhaust", out var e), Is.True);
		Assert.That(e!.TitleKey, Is.EqualTo("KW_EXHAUST_TITLE"));
	}

	[Test]
	public void Register_same_id_overwrites()
	{
		var catalog = new KeywordCatalog();
		catalog.Register(new KeywordEntry("exhaust", "A", "B"));
		catalog.Register(new KeywordEntry("exhaust", "C", "D"));
		Assert.That(catalog.TryGet("exhaust", out var e), Is.True);
		Assert.That(e!.TitleKey, Is.EqualTo("C"));
		Assert.That(e.DescKey, Is.EqualTo("D"));
	}

	[Test]
	public void Unregister_removes_entry()
	{
		var catalog = new KeywordCatalog();
		catalog.Register(new KeywordEntry("retain", "T", "D"));
		catalog.Unregister("retain");
		Assert.That(catalog.TryGet("retain", out _), Is.False);
	}

	[Test]
	public void Clear_removes_all()
	{
		var catalog = new KeywordCatalog();
		catalog.Register(new KeywordEntry("a", "t", "d"));
		catalog.Clear();
		Assert.That(catalog.TryGet("a", out _), Is.False);
	}
}

[TestFixture]
public sealed class KeywordTextFormatterTests
{
	[Test]
	public void ApplyParams_replaces_named_placeholders()
	{
		var text = KeywordTextFormatter.ApplyParams(
			"造成 {amount} 点伤害",
			new Dictionary<string, string> { ["amount"] = "5" });
		Assert.That(text, Is.EqualTo("造成 5 点伤害"));
	}

	[Test]
	public void ApplyParams_missing_keeps_placeholder()
	{
		var text = KeywordTextFormatter.ApplyParams(
			"造成 {amount} 点伤害",
			new Dictionary<string, string>());
		Assert.That(text, Is.EqualTo("造成 {amount} 点伤害"));
	}

	[Test]
	public void ApplyParams_null_or_empty_params_keeps_text()
	{
		Assert.That(KeywordTextFormatter.ApplyParams("无参数", null), Is.EqualTo("无参数"));
	}
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~KeywordCatalogTests|FullyQualifiedName~KeywordTextFormatterTests" --no-restore
```

Expected: 编译失败（类型不存在）或测试失败。

- [ ] **Step 3: 实现类型**

`TipSide.cs`：

```csharp
namespace KemoCard.Frame.Content.Keywords;

public enum TipSide
{
	Left = 0,
	Right = 1,
}
```

`KeywordEntry.cs`：

```csharp
namespace KemoCard.Frame.Content.Keywords;

public sealed class KeywordEntry
{
	public KeywordEntry(string id, string titleKey, string descKey)
	{
		Id = id ?? "";
		TitleKey = titleKey ?? "";
		DescKey = descKey ?? "";
	}

	public string Id { get; }
	public string TitleKey { get; }
	public string DescKey { get; }
}
```

`KeywordTipRequest.cs`：

```csharp
namespace KemoCard.Frame.Content.Keywords;

public sealed class KeywordTipRequest
{
	public KeywordTipRequest(string keywordId, IReadOnlyDictionary<string, string>? parameters = null)
	{
		KeywordId = keywordId ?? "";
		Parameters = parameters ?? new Dictionary<string, string>();
	}

	public string KeywordId { get; }
	public IReadOnlyDictionary<string, string> Parameters { get; }
}
```

`KeywordTextFormatter.cs`：用正则 `\{([A-Za-z_][A-Za-z0-9_]*)\}` 替换；缺键保留原文。

`KeywordCatalog.cs`：`Dictionary<string, KeywordEntry>`；`Register` 若已存在则 `GD.PushWarning` 后覆盖；`Unregister` / `TryGet` / `Clear`；提供静态 `Shared` 供运行时使用。

- [ ] **Step 4: 再跑测试，确认通过**

- [ ] **Step 5: Commit**

```powershell
git add Src/frame/content/keywords Tests/kemo_card.Ui.Tests/KeywordCatalogTests.cs
git commit -m "feat(content): 新增词条 Catalog 与命名参数格式化"
```

---

### Task 2: 内置词条 + 本地化

**Files:**
- Create: `Src/mod/global/Def/BuiltinKeywords.cs`
- Modify: `Resource/Locale/strings.csv`
- Modify: `Src/mod/ModFactory.cs` 或 `MainRoot.cs`（Bootstrap 后调用 `BuiltinKeywords.RegisterAll(KeywordCatalog.Shared)`）

- [ ] **Step 1: 追加 CSV**

```
KW_EXHAUST_TITLE,消耗,Exhaust
KW_EXHAUST_DESC,使用后移出战斗。,Remove from combat when played.
KW_RETAIN_TITLE,保留,Retain
KW_RETAIN_DESC,回合结束时留在手牌。,Keep in hand at end of turn.
KW_DEAL_DAMAGE_TITLE,伤害,Damage
KW_DEAL_DAMAGE_DESC,造成 {amount} 点伤害。,Deal {amount} damage.
```

- [ ] **Step 2: BuiltinKeywords.RegisterAll**

注册 `exhaust` / `retain` / `deal_damage` 三条。

- [ ] **Step 3: Bootstrap 调用 RegisterAll**

- [ ] **Step 4: Commit**

```powershell
git commit -m "feat(content): 注册内置词条与本地化文案"
```

---

### Task 3: KeywordTipPanel + KeywordTipService

**Files:**
- Create: `Src/mod/global/Ui/Tip/KeywordTipPanel.cs` / `.tscn`
- Create: `Src/mod/global/Ui/Tip/KeywordTipService.cs`
- Create: `Src/mod/global/Ui/Tip/KeywordTipLayer.tscn`（CanvasLayer + VBoxContainer）
- Modify: `Src/MainRoot.cs` / `.tscn`：AddChild TipLayer

**API：**

```csharp
public partial class KeywordTipService : CanvasLayer
{
	public static KeywordTipService? Current { get; private set; }

	public void ShowTips(Control anchor, IReadOnlyList<KeywordTipRequest> tips, TipSide preferSide = TipSide.Right);
	public void HideTips();
	public void HideTips(Control anchor);
}
```

**定位逻辑：**
1. 解析 tips → Catalog.TryGet；失败则 Warning 跳过
2. Title = `Localization.Tr(TitleKey)`；Desc = `ApplyParams(Localization.Tr(DescKey), params)`
3. 实例化 Panel 加入内部 VBox；`ResetSize` 后量宽高
4. 锚点 `GetGlobalRect()`；优先侧：`x = right + gap` 或 `x = left - tipWidth - gap`；`y` 对齐锚点顶部
5. 若优先侧整体超出视口，改用对侧；若仍超出则 clamp 到视口内
6. `_EnterTree` 设 `Current = this`；`_ExitTree` 清空

- [ ] **Step 1: 实现 Panel 场景**（Title Label + Desc Label，布局在编辑器完成）
- [ ] **Step 2: 实现 KeywordTipService**
- [ ] **Step 3: MainRoot 挂载 TipLayer**
- [ ] **Step 4: 引擎内手动冒烟（可选）**
- [ ] **Step 5: Commit**

```powershell
git commit -m "feat(ui): 实现词条提示 TipService 与 Panel"
```

---

### Task 4: BaseButton

**Files:**
- Modify: `Src/mod/global/Ui/Comp/BaseButton.cs`

```csharp
public partial class BaseButton : Button
{
	[Export] public bool EnableHoverScale { get; set; } = true;
	[Export] public bool EnablePressScale { get; set; } = true;
	[Export] public float HoverScale { get; set; } = 1.05f;
	[Export] public float PressScale { get; set; } = 0.95f;
	[Export] public float AnimDuration { get; set; } = 0.08f;
	[Export] public float TipDelaySec { get; set; } = 0.15f;
	[Export] public TipSide PreferTipSide { get; set; } = TipSide.Right;
	[Export] public string[] KeywordIds { get; set; } = []; // 无参词条快捷配置

	public void SetKeywordTips(IReadOnlyList<KeywordTipRequest> tips);
	// hover/focus → 延迟 Show；exit → Hide
	// PivotOffset = Size/2 以保证缩放居中
}
```

- [ ] **Step 1: 实现动画与 tip 触发**
- [ ] **Step 2: `dotnet format` 该文件**
- [ ] **Step 3: Commit**

```powershell
git commit -m "feat(ui): 实现 BaseButton 动画与词条提示触发"
```

---

### Task 5: BaseCardItem Tip 入口

**Files:**
- Modify: `Src/mod/global/Ui/Comp/BaseCardItem.cs`

```csharp
public void ShowTips(IReadOnlyList<KeywordTipRequest> tips, TipSide preferSide = TipSide.Right)
public void HideTips()
```

内部调用 `KeywordTipService.Current`；Current 为空则 Warning。

- [ ] **Step 1: 添加 API**
- [ ] **Step 2: format + Commit**

```powershell
git commit -m "feat(ui): BaseCardItem 增加词条提示调用入口"
```

---

### Task 6: 验证收尾

- [ ] **Step 1: 跑全量相关测试**

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~Keyword"
```

- [ ] **Step 2: 对照 spec §8 验收清单自检**
- [ ] **Step 3: 若有未提交改动则提交**

---

## Spec 覆盖自检

| Spec 项 | Task |
|---------|------|
| Catalog 动态注册 / 后写覆盖 | 1 |
| 命名参数 / 缺参保留 | 1 |
| 内置词条 + 本地化 | 2 |
| TipService 堆叠 / 左右翻转 | 3 |
| BaseButton 动画 + hover/focus | 4 |
| BaseCardItem 入口 | 5 |
| 单测 | 1, 6 |
| 不做 EContentCategory / 多锚点 / 上下翻转 | 明确不在任务中 |
