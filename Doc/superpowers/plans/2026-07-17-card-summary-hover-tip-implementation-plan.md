# 卡牌摘要悬停 Tip Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 `CardSummaryBuilder` 卡牌摘要文案，扩展 `KeywordTipService.ShowCustomTips`，并在图鉴 `BaseCardItem` 上启用悬停 tip。

**Architecture:** 纯逻辑 Builder 产出 Title/Body；Tip 服务增加自由文案入口复用面板定位；`BaseCardItem.EnableHoverTip` 默认关，仅 `CodexDlg` 打开。费用后缀经可扩展 `TryGetCostTipSuffixKey` / `FormatCostForTip` 解析。

**Tech Stack:** Godot 4.6 Mono / C# / NUnit

**Spec:** `Doc/superpowers/specs/2026-07-17-card-summary-hover-tip-design.md`

## Global Constraints

- 面向用户文案必须走本地化键；日志可用明文
- 代码只用 C#；组合优先于继承
- `Src/frame/` 不得依赖 `Src/mod/`
- 改完逻辑后对改动过的 `.cs` 执行格式化
- Git 提交说明使用简体中文（仅在用户要求提交时执行 commit 步骤）

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `Resource/Locale/strings.csv` | tip 费用后缀与专属前缀键 |
| `Src/mod/global/Def/Definitions.cs`（`CardUiDefinitions`） | `TryGetCostTipSuffixKey` / `FormatCostForTip` |
| `Src/mod/global/Ui/CardSummaryBuilder.cs` | `CardSummaryTip` + `Build` + BBCode 剥离 |
| `Tests/kemo_card.Ui.Tests/CardSummaryBuilderTests.cs` | 摘要/费用/剥离/专属单测 |
| `Src/mod/global/Ui/Tip/KeywordTipService.cs` | `ShowCustomTips` |
| `Src/mod/global/Ui/Comp/BaseCardItem.cs` | 可选悬停 tip |
| `Src/mod/global/Ui/CodexDlg.cs` | 卡槽 `EnableHoverTip = true` |

只读依赖：`CardDescBuilder`、`CodexFilterDefinitions`、`GameDefinitionStore.Characters`、`Localization`、`AppRoot`。

---

### Task 1: 本地化键 + 费用 tip 格式化接口

**Files:**
- Modify: `Resource/Locale/strings.csv`
- Modify: `Src/mod/global/Def/Definitions.cs`
- Create: `Tests/kemo_card.Ui.Tests/CardSummaryBuilderTests.cs`（本 Task 先写费用相关测试）

**Interfaces:**
- Produces:
  - `CardUiDefinitions.TryGetCostTipSuffixKey(ECostType costType, out string key) -> bool`
  - `CardUiDefinitions.FormatCostForTip(ECostType costType, int cost, Func<string, string> translate) -> string`
  - 常量键：`UI_CARD_TIP_COST_SUFFIX`、`UI_CARD_TIP_COST_SUFFIX_X`、`UI_CARD_TIP_EXCLUSIVE_PREFIX`

- [ ] **Step 1: 在 strings.csv 追加三行**

在 `UI_ROLE_ELEMENTIST` 附近（或文件末尾 UI 区块）追加：

```csv
UI_CARD_TIP_COST_SUFFIX,费, Cost
UI_CARD_TIP_COST_SUFFIX_X,费, Cost
UI_CARD_TIP_EXCLUSIVE_PREFIX,专属于此角色：,Exclusive to:
```

注意：英文 ` Cost` 前有空格，保证 `3 Cost` / `X Cost`。

- [ ] **Step 2: 写失败测试（费用格式）**

创建 `Tests/kemo_card.Ui.Tests/CardSummaryBuilderTests.cs`：

```csharp
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Def;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class CardSummaryBuilderTests
{
	private static string Tr(string key) => key switch
	{
		"UI_CARD_TIP_COST_SUFFIX" => "费",
		"UI_CARD_TIP_COST_SUFFIX_X" => "费",
		"UI_CARD_TIP_EXCLUSIVE_PREFIX" => "专属于此角色：",
		"UI_ELEMENT_RED" => "红",
		"UI_ELEMENT_BLUE" => "蓝",
		"UI_ROLE_WARRIOR" => "战士",
		"card.strike.name" => "打击",
		"d1" => "造成 6 点伤害。",
		"d2" => "[url=kw:exhaust]消耗[/url]",
		_ => key,
	};

	[Test]
	public void FormatCostForTip_energy_appends_suffix()
	{
		Assert.That(
			CardUiDefinitions.FormatCostForTip(ECostType.Energy, 3, Tr),
			Is.EqualTo("3费"));
	}

	[Test]
	public void FormatCostForTip_x_uses_x_suffix()
	{
		Assert.That(
			CardUiDefinitions.FormatCostForTip(ECostType.X, 0, Tr),
			Is.EqualTo("X费"));
	}

	[Test]
	public void FormatCostForTip_none_returns_empty()
	{
		Assert.That(
			CardUiDefinitions.FormatCostForTip(ECostType.None, 5, Tr),
			Is.EqualTo(""));
	}

	[Test]
	public void TryGetCostTipSuffixKey_unknown_falls_back_to_normal()
	{
		Assert.That(CardUiDefinitions.TryGetCostTipSuffixKey(ECostType.Health, out var key), Is.True);
		Assert.That(key, Is.EqualTo("UI_CARD_TIP_COST_SUFFIX"));
	}
}
```

- [ ] **Step 3: 运行测试确认失败**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~CardSummaryBuilderTests" -v n`

Expected: 编译失败或 FAIL（`FormatCostForTip` / `TryGetCostTipSuffixKey` 不存在）

- [ ] **Step 4: 在 CardUiDefinitions 实现接口**

在 `Src/mod/global/Def/Definitions.cs` 的 `CardUiDefinitions` 内、`FormatCost` 旁追加：

```csharp
public const string CostTipSuffixKey = "UI_CARD_TIP_COST_SUFFIX";
public const string CostTipSuffixXKey = "UI_CARD_TIP_COST_SUFFIX_X";
public const string ExclusivePrefixKey = "UI_CARD_TIP_EXCLUSIVE_PREFIX";

/// <summary>按费用类型取 tip 后缀本地化键；未知类型回退到正常后缀。</summary>
public static bool TryGetCostTipSuffixKey(ECostType costType, out string key)
{
	switch (costType)
	{
		case ECostType.None:
			key = "";
			return false;
		case ECostType.X:
			key = CostTipSuffixXKey;
			return true;
		default:
			key = CostTipSuffixKey;
			return true;
	}
}

public static string FormatCostForTip(ECostType costType, int cost, Func<string, string> translate)
{
	ArgumentNullException.ThrowIfNull(translate);
	if (costType == ECostType.None || !TryGetCostTipSuffixKey(costType, out var suffixKey))
	{
		return "";
	}

	var suffix = translate(suffixKey);
	return costType == ECostType.X
		? $"X{suffix}"
		: $"{cost}{suffix}";
}
```

- [ ] **Step 5: 格式化改动过的 cs 并跑测试**

Run: `dotnet format`（或 IDE Format Document）针对 `Definitions.cs`；再跑 Step 3 的 filter。

Expected: PASS

- [ ] **Step 6: Commit（仅当用户要求提交时）**

```bash
git add Resource/Locale/strings.csv Src/mod/global/Def/Definitions.cs Tests/kemo_card.Ui.Tests/CardSummaryBuilderTests.cs
git commit -m "$(cat <<'EOF'
补充卡牌 tip 费用后缀本地化与 FormatCostForTip 接口

EOF
)"
```

---

### Task 2: CardSummaryBuilder

**Files:**
- Create: `Src/mod/global/Ui/CardSummaryBuilder.cs`
- Modify: `Tests/kemo_card.Ui.Tests/CardSummaryBuilderTests.cs`

**Interfaces:**
- Consumes: `CardUiDefinitions.FormatCostForTip`、`CodexFilterDefinitions.TryGetElementLocaleKey` / `TryGetRoleLocaleKey`、`CardDescBuilder.Build`
- Produces:
  - `readonly record struct CardSummaryTip(string Title, string Body)`
  - `CardSummaryBuilder.Build(CardDto, Func<string, SkillDto?>, Func<string, string>, Func<string, string?>) -> CardSummaryTip`
  - `CardSummaryBuilder.StripRichText(string) -> string`（供测试与内部使用）

- [ ] **Step 1: 追加失败测试**

在 `CardSummaryBuilderTests` 中追加：

```csharp
using KemoCard.Mod.Global.Ui;

[Test]
public void StripRichText_removes_bbcode_keeps_inner()
{
	Assert.That(
		CardSummaryBuilder.StripRichText("造成[url=kw:exhaust]消耗[/url]伤害"),
		Is.EqualTo("造成消耗伤害"));
}

[Test]
public void Build_formats_meta_effect_and_exclusive_lines()
{
	var card = new CardDto
	{
		Id = "strike",
		DisplayNameId = "card.strike.name",
		CostType = ECostType.Energy,
		Cost = 3,
		Element = (int)EElement.Red,
		Role = ERole.Warrior,
		IsExclusive = true,
		SkillRefs = [new SkillRefDto { SkillId = "s1" }],
	};

	var tip = CardSummaryBuilder.Build(
		card,
		id => id == "s1" ? new SkillDto { Id = "s1", DescId = "d1" } : null,
		Tr,
		cardId => cardId == "strike" ? "可萝" : null);

	Assert.That(tip.Title, Is.EqualTo("打击"));
	Assert.That(tip.Body, Is.EqualTo("3费 红 战士\n造成 6 点伤害。\n专属于此角色：可萝"));
}

[Test]
public void Build_omits_exclusive_when_resolver_returns_null()
{
	var card = new CardDto
	{
		DisplayNameId = "card.strike.name",
		CostType = ECostType.X,
		Cost = 0,
		Element = 0,
		Role = ERole.None,
		IsExclusive = false,
		SkillRefs = [new SkillRefDto { SkillId = "s1" }],
	};

	var tip = CardSummaryBuilder.Build(
		card,
		id => id == "s1" ? new SkillDto { Id = "s1", DescId = "d1" } : null,
		Tr,
		_ => null);

	Assert.That(tip.Title, Is.EqualTo("打击"));
	Assert.That(tip.Body, Is.EqualTo("X费\n造成 6 点伤害。"));
}

[Test]
public void Build_strips_bbcode_in_effect()
{
	var card = new CardDto
	{
		DisplayNameId = "card.strike.name",
		CostType = ECostType.None,
		SkillRefs = [new SkillRefDto { SkillId = "s2" }],
	};

	var tip = CardSummaryBuilder.Build(
		card,
		id => id == "s2" ? new SkillDto { Id = "s2", DescId = "d2" } : null,
		Tr,
		_ => null);

	Assert.That(tip.Body, Is.EqualTo("消耗"));
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~CardSummaryBuilderTests" -v n`

Expected: FAIL（`CardSummaryBuilder` 不存在）

- [ ] **Step 3: 实现 CardSummaryBuilder**

创建 `Src/mod/global/Ui/CardSummaryBuilder.cs`：

```csharp
using System.Text;
using System.Text.RegularExpressions;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Def;

namespace KemoCard.Mod.Global.Ui;

public readonly record struct CardSummaryTip(string Title, string Body);

public static partial class CardSummaryBuilder
{
	[GeneratedRegex(@"\[url=[^\]]*\](.*?)\[/url\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex UrlTagRegex();

	[GeneratedRegex(@"\[/?[^\]]+\]")]
	private static partial Regex OtherTagRegex();

	public static CardSummaryTip Build(
		CardDto card,
		Func<string, SkillDto?> resolveSkill,
		Func<string, string> translate,
		Func<string, string?> resolveExclusiveCharacterName)
	{
		ArgumentNullException.ThrowIfNull(card);
		ArgumentNullException.ThrowIfNull(resolveSkill);
		ArgumentNullException.ThrowIfNull(translate);
		ArgumentNullException.ThrowIfNull(resolveExclusiveCharacterName);

		var title = string.IsNullOrWhiteSpace(card.DisplayNameId)
			? ""
			: translate(card.DisplayNameId);

		var lines = new List<string>();

		var meta = BuildMetaLine(card, translate);
		if (!string.IsNullOrEmpty(meta))
		{
			lines.Add(meta);
		}

		var effect = StripRichText(CardDescBuilder.Build(card, resolveSkill, translate));
		if (!string.IsNullOrWhiteSpace(effect))
		{
			lines.Add(effect.Trim());
		}

		if (card.IsExclusive)
		{
			var exclusiveName = resolveExclusiveCharacterName(card.Id);
			if (!string.IsNullOrWhiteSpace(exclusiveName))
			{
				lines.Add(translate(CardUiDefinitions.ExclusivePrefixKey) + exclusiveName);
			}
		}

		return new CardSummaryTip(title, string.Join("\n", lines));
	}

	public static string StripRichText(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}

		try
		{
			var withoutUrl = UrlTagRegex().Replace(text, "$1");
			return OtherTagRegex().Replace(withoutUrl, "");
		}
		catch
		{
			return text;
		}
	}

	private static string BuildMetaLine(CardDto card, Func<string, string> translate)
	{
		var parts = new List<string>();

		var cost = CardUiDefinitions.FormatCostForTip(card.CostType, card.Cost, translate);
		if (!string.IsNullOrEmpty(cost))
		{
			parts.Add(cost);
		}

		var elements = FormatElements(card.Element, translate);
		if (!string.IsNullOrEmpty(elements))
		{
			parts.Add(elements);
		}

		if (card.Role != ERole.None
			&& CodexFilterDefinitions.TryGetRoleLocaleKey(card.Role, out var roleKey)
			&& card.Role != ERole.None)
		{
			parts.Add(translate(roleKey));
		}

		return string.Join(" ", parts);
	}

	private static string FormatElements(int elementFlags, Func<string, string> translate)
	{
		if (elementFlags == 0)
		{
			return "";
		}

		var names = new List<string>();
		foreach (EElement e in Enum.GetValues<EElement>())
		{
			if (e == EElement.None)
			{
				continue;
			}

			if ((elementFlags & (int)e) == 0)
			{
				continue;
			}

			if (CodexFilterDefinitions.TryGetElementLocaleKey(e, out var key))
			{
				names.Add(translate(key));
			}
		}

		return names.Count == 0 ? "" : string.Join("、", names);
	}
}
```

注意：`BuildMetaLine` 里对 `ERole.None` 只判断一次即可（实现时去掉重复判断）。`TryGetRoleLocaleKey(None)` 会返回 `UI_ROLE_NONE`，因此必须先跳过 `None`。

- [ ] **Step 4: 将文件加入 csproj（若项目非通配）并跑测试**

若 `kemo_card.csproj` 已通配 `Src/**/*.cs`，无需改项目文件。

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~CardSummaryBuilderTests" -v n`

Expected: PASS

- [ ] **Step 5: 格式化 `CardSummaryBuilder.cs` 后 Commit（仅当用户要求）**

```bash
git add Src/mod/global/Ui/CardSummaryBuilder.cs Tests/kemo_card.Ui.Tests/CardSummaryBuilderTests.cs
git commit -m "$(cat <<'EOF'
实现 CardSummaryBuilder 生成卡牌悬停摘要文案

EOF
)"
```

---

### Task 3: KeywordTipService.ShowCustomTips

**Files:**
- Modify: `Src/mod/global/Ui/Tip/KeywordTipService.cs`

**Interfaces:**
- Produces: `void ShowCustomTips(Control anchor, IReadOnlyList<(string Title, string Desc)> tips, TipSide preferSide = TipSide.Right)`

- [ ] **Step 1: 抽取公共显示核心**

将现有 `ShowTips` 中「清面板 → 加 panel → 定位」抽成私有方法，例如：

```csharp
private void ShowPanels(
	Control anchor,
	IReadOnlyList<(string Title, string Desc)> panels,
	TipSide preferSide)
{
	// 与现有 ShowTips 相同的锚点绑定 / ClearPanels / EnsurePanelScene /
	// Instantiate KeywordTipPanel + SetContent / DeferredPositionStack
	// panels 为空则 HideTips()
}
```

- [ ] **Step 2: 实现 ShowCustomTips**

```csharp
public void ShowCustomTips(
	Control anchor,
	IReadOnlyList<(string Title, string Desc)> tips,
	TipSide preferSide = TipSide.Right)
{
	if (tips == null || tips.Count == 0)
	{
		HideTips();
		return;
	}

	var panels = new List<(string Title, string Desc)>(tips.Count);
	foreach (var tip in tips)
	{
		if (string.IsNullOrWhiteSpace(tip.Title) && string.IsNullOrWhiteSpace(tip.Desc))
		{
			continue;
		}

		panels.Add((tip.Title ?? "", tip.Desc ?? ""));
	}

	ShowPanels(anchor, panels, preferSide);
}
```

- [ ] **Step 3: 改写 ShowTips 走同一核心**

`ShowTips` 仍查 `KeywordCatalog`，组装 `(title, desc)` 列表后调用 `ShowPanels`。

- [ ] **Step 4: 格式化并编译**

Run: `dotnet build kemo_card.csproj -v q`（或解决方案主项目）

Expected: 成功，无新警告相关错误

- [ ] **Step 5: Commit（仅当用户要求）**

```bash
git add Src/mod/global/Ui/Tip/KeywordTipService.cs
git commit -m "$(cat <<'EOF'
KeywordTipService 支持自由文案 ShowCustomTips

EOF
)"
```

---

### Task 4: BaseCardItem 悬停 tip + CodexDlg 开启

**Files:**
- Modify: `Src/mod/global/Ui/Comp/BaseCardItem.cs`
- Modify: `Src/mod/global/Ui/CodexDlg.cs`

**Interfaces:**
- Consumes: `CardSummaryBuilder.Build`、`KeywordTipService.ShowCustomTips`、`GameDefinitionStore`
- Produces: `EnableHoverTip` / `TipDelaySec` / `PreferTipSide`；图鉴启用悬停

- [ ] **Step 1: BaseCardItem 增加 Export 与悬停字段**

在类字段区追加：

```csharp
[Export] public bool EnableHoverTip { get; set; }
[Export] public float TipDelaySec { get; set; } = 0.15f;
[Export] public TipSide PreferTipSide { get; set; } = TipSide.Right;

private Tween? _tipDelayTween;
private bool _hoverTipActive;
```

- [ ] **Step 2: _Ready / _ExitTree 绑定**

在 `_Ready` 中：

```csharp
MouseEntered += OnHoverTipEntered;
MouseExited += OnHoverTipExited;
```

新增 `_ExitTree`（若尚无）：

```csharp
public override void _ExitTree()
{
	CancelHoverTipDelay();
	KeywordTipService.Current?.HideTips(this);
	MouseEntered -= OnHoverTipEntered;
	MouseExited -= OnHoverTipExited;
	base._ExitTree();
}
```

- [ ] **Step 3: 实现悬停逻辑 region**

```csharp
#region 悬停摘要 Tip

private void OnHoverTipEntered()
{
	if (!EnableHoverTip || _card == null)
	{
		return;
	}

	_hoverTipActive = true;
	ScheduleHoverTip();
}

private void OnHoverTipExited()
{
	_hoverTipActive = false;
	CancelHoverTipDelay();
	KeywordTipService.Current?.HideTips(this);
}

private void ScheduleHoverTip()
{
	CancelHoverTipDelay();
	if (TipDelaySec <= 0f)
	{
		ShowHoverTipNow();
		return;
	}

	_tipDelayTween = CreateTween();
	_tipDelayTween.TweenInterval(TipDelaySec);
	_tipDelayTween.TweenCallback(Callable.From(ShowHoverTipNow));
}

private void CancelHoverTipDelay()
{
	_tipDelayTween?.Kill();
	_tipDelayTween = null;
}

private void ShowHoverTipNow()
{
	if (!_hoverTipActive || !EnableHoverTip || _card == null)
	{
		return;
	}

	var service = KeywordTipService.Current;
	if (service == null)
	{
		AppLog.Warning("BaseCardItem: KeywordTipService.Current 为空，无法显示卡牌摘要提示。", "BaseCardItem");
		return;
	}

	GameDefinitionStore store;
	try
	{
		store = AppRoot.Services.ContentModPipeline.Registry.Store;
	}
	catch (InvalidOperationException)
	{
		AppLog.Warning("BaseCardItem: AppRoot 未初始化，无法构建卡牌摘要。", "BaseCardItem");
		return;
	}

	var tip = CardSummaryBuilder.Build(
		_card,
		id => store.TryGetSkill(id, out var skill) ? skill : null,
		Localization.Tr,
		cardId => ResolveExclusiveCharacterName(store, cardId));

	if (string.IsNullOrWhiteSpace(tip.Title) && string.IsNullOrWhiteSpace(tip.Body))
	{
		return;
	}

	service.ShowCustomTips(this, [(tip.Title, tip.Body)], PreferTipSide);
}

private static string? ResolveExclusiveCharacterName(GameDefinitionStore store, string cardId)
{
	foreach (var character in store.Characters.Values)
	{
		if (character.Cards == null || character.Cards.Count == 0)
		{
			continue;
		}

		if (!character.Cards.Contains(cardId))
		{
			continue;
		}

		if (string.IsNullOrWhiteSpace(character.DisplayNameId))
		{
			return null;
		}

		return Localization.Tr(character.DisplayNameId);
	}

	return null;
}

#endregion
```

需补充 using：`KemoCard.Fixed.Godot`（若尚无 Localization）、已有 Tip / Content 命名空间。

- [ ] **Step 4: CodexDlg 启用**

在 `CacheCardSlots` 末尾：

```csharp
foreach (var item in _cardSlots)
{
	item.EnableHoverTip = true;
}
```

- [ ] **Step 5: 格式化改动文件并编译 + 单测**

Run:

```bash
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~CardSummaryBuilderTests|FullyQualifiedName~CardDescBuilderTests" -v n
dotnet build
```

Expected: PASS / 编译成功

- [ ] **Step 6: 手动验证（图鉴）**

1. 运行游戏，打开图鉴
2. 鼠标悬停卡牌约 0.15s，出现 tip：卡名 / `费用 属性 职业` / 效果 /（若有）专属行
3. 移开鼠标 tip 消失
4. 打开卡牌详情：内嵌卡面不应出现摘要 tip（`EnableHoverTip` 默认 false）

- [ ] **Step 7: Commit（仅当用户要求）**

```bash
git add Src/mod/global/Ui/Comp/BaseCardItem.cs Src/mod/global/Ui/CodexDlg.cs
git commit -m "$(cat <<'EOF'
图鉴卡牌悬停显示摘要 tip，默认仅 Codex 启用

EOF
)"
```

---

## Spec 覆盖自检

| Spec 条目 | Task |
|-----------|------|
| CardSummaryBuilder Title/Body | Task 2 |
| 费用 属性 职业同行空格 | Task 2 |
| 3费/X费 + 可扩展后缀接口 | Task 1 |
| 效果 + BBCode 剥离 | Task 2 |
| 专属前缀无【】 | Task 1 CSV + Task 2 |
| ShowCustomTips | Task 3 |
| EnableHoverTip 默认关 | Task 4 |
| CodexDlg 开启 | Task 4 |
| 单测费用/拼接/剥离/专属 | Task 1–2 |
| 详情内嵌卡不开启 tip | Task 4（默认 false，手动验证） |
