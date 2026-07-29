# 图鉴卡牌过滤与分页 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成 `CodexDlg` 卡牌 Tab 的多条件过滤、文本搜索、条件列表管理，以及固定 8 格 + `BasePager` 分页展示。

**Architecture:** `CardCodexQuery` 提供纯过滤/切片（可单测，经 `Func` 注入翻译与技能查找）；`CodexFilterDefinitions` 提供字段/操作/枚举展示的本地化键；`CodexDlg` 只做控件绑定、条件状态与分页刷新。数据源为 `AppRoot.Services.ContentModPipeline.Registry.Store`。

**Tech Stack:** Godot 4.6 Mono / C# / NUnit

**Spec:** `Doc/superpowers/specs/2026-07-10-codex-card-filter-design.md`

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `Src/mod/global/Ui/CardCodexQuery.cs` | 过滤字段/操作枚举、`CardFilterCondition`、匹配、过滤、分页切片、标签收集 |
| `Src/mod/global/Def/CodexFilterDefinitions.cs` | 字段/操作/元素/角色/费用类型 → 本地化键 |
| `Resource/Locale/strings.csv` | 新增图鉴过滤相关翻译 |
| `Src/mod/global/Ui/CodexDlg.cs` | UI 编排：下拉联动、条件增删、文本搜索、列表与分页 |
| `Src/mod/global/Ui/CodexDlg.tscn` | `[Export]` `node_paths` 绑定 |
| `Tests/kemo_card.Ui.Tests/CardCodexQueryTests.cs` | 纯逻辑单测 |

依赖（只读，不改）：`BaseCardItem`、`BasePager`、`CardDto`、`SkillDto`、`GameDefinitionStore`、`AppRoot`、`Localization`。

---

### Task 1: 本地化键与 CodexFilterDefinitions

**Files:**
- Modify: `Resource/Locale/strings.csv`
- Create: `Src/mod/global/Def/CodexFilterDefinitions.cs`

- [ ] **Step 1: 追加 strings.csv 键**

在 `Resource/Locale/strings.csv` 末尾追加（保持 `keys,zh_CN,en` 三列）：

```csv
UI_CODEX_FILTER_CARD_TYPE,卡牌类型,Card Type
UI_CODEX_FILTER_COST,费用,Cost
UI_CODEX_FILTER_ELEMENT,元素,Element
UI_CODEX_FILTER_ROLE,角色,Role
UI_CODEX_FILTER_COST_TYPE,费用类型,Cost Type
UI_CODEX_FILTER_TAG,标签,Tag
UI_CODEX_OP_EQ,等于,Equals
UI_CODEX_OP_NE,不等于,Not Equals
UI_CODEX_OP_LE,小于等于,Less Or Equal
UI_CODEX_OP_GE,大于等于,Greater Or Equal
UI_CODEX_OP_CONTAINS,包含,Contains
UI_CODEX_OP_EXACT,等于,Exact
UI_ELEMENT_RED,红,Red
UI_ELEMENT_BLUE,蓝,Blue
UI_ELEMENT_GREEN,绿,Green
UI_ELEMENT_YELLOW,黄,Yellow
UI_ELEMENT_YIN,阴,Yin
UI_ELEMENT_YANG,阳,Yang
UI_COST_TYPE_NONE,无,None
UI_COST_TYPE_ENERGY,能量,Energy
UI_COST_TYPE_HEALTH,生命,Health
UI_COST_TYPE_GOLD,金币,Gold
UI_COST_TYPE_DISCARD,弃牌,Discard
UI_COST_TYPE_X,X,X
UI_ROLE_NONE,无,None
UI_ROLE_WARRIOR,战士,Warrior
UI_ROLE_WIZARD,术士,Wizard
UI_ROLE_HEALER,治疗者,Healer
UI_ROLE_GUARD,守护者,Guard
UI_ROLE_SHIELD,护盾,Shield
UI_ROLE_CONTROLLER,控制者,Controller
UI_ROLE_SUPPORT,支援者,Support
UI_ROLE_CARD_PLAYER,卡牌手,Card Player
UI_ROLE_SWORD_MAN,剑士,Sword Man
UI_ROLE_MAGE,法师,Mage
UI_ROLE_ALCHEMIST,炼金术士,Alchemist
UI_ROLE_ELEMENTIST,元素师,Elementist
```

说明：`UI_CODEX_OP_EXACT` 与 `UI_CODEX_OP_EQ` 中文都可显示「等于」，但键分离以便元素/标签「精确等于」语义与枚举「等于」区分（英文 Exact vs Equals）。

- [ ] **Step 2: 创建 CodexFilterDefinitions.cs**

```csharp
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Ui;

namespace KemoCard.Mod.Global.Def;

public static class CodexFilterDefinitions
{
	public static string GetFieldLocaleKey(ECardFilterField field) => field switch
	{
		ECardFilterField.CardType => "UI_CODEX_FILTER_CARD_TYPE",
		ECardFilterField.Cost => "UI_CODEX_FILTER_COST",
		ECardFilterField.Element => "UI_CODEX_FILTER_ELEMENT",
		ECardFilterField.Role => "UI_CODEX_FILTER_ROLE",
		ECardFilterField.CostType => "UI_CODEX_FILTER_COST_TYPE",
		ECardFilterField.Tag => "UI_CODEX_FILTER_TAG",
		_ => field.ToString(),
	};

	public static string GetOpLocaleKey(ECardFilterOp op) => op switch
	{
		ECardFilterOp.Equal => "UI_CODEX_OP_EQ",
		ECardFilterOp.NotEqual => "UI_CODEX_OP_NE",
		ECardFilterOp.LessOrEqual => "UI_CODEX_OP_LE",
		ECardFilterOp.GreaterOrEqual => "UI_CODEX_OP_GE",
		ECardFilterOp.Contains => "UI_CODEX_OP_CONTAINS",
		ECardFilterOp.Exact => "UI_CODEX_OP_EXACT",
		_ => op.ToString(),
	};

	public static bool TryGetElementLocaleKey(EElement element, out string key)
	{
		key = element switch
		{
			EElement.Red => "UI_ELEMENT_RED",
			EElement.Blue => "UI_ELEMENT_BLUE",
			EElement.Green => "UI_ELEMENT_GREEN",
			EElement.Yellow => "UI_ELEMENT_YELLOW",
			EElement.Yin => "UI_ELEMENT_YIN",
			EElement.Yang => "UI_ELEMENT_YANG",
			_ => "",
		};
		return key.Length > 0;
	}

	public static bool TryGetCostTypeLocaleKey(ECostType costType, out string key)
	{
		key = costType switch
		{
			ECostType.None => "UI_COST_TYPE_NONE",
			ECostType.Energy => "UI_COST_TYPE_ENERGY",
			ECostType.Health => "UI_COST_TYPE_HEALTH",
			ECostType.Gold => "UI_COST_TYPE_GOLD",
			ECostType.Discard => "UI_COST_TYPE_DISCARD",
			ECostType.X => "UI_COST_TYPE_X",
			_ => "",
		};
		return key.Length > 0;
	}

	public static bool TryGetRoleLocaleKey(ERole role, out string key)
	{
		key = role switch
		{
			ERole.None => "UI_ROLE_NONE",
			ERole.Warrior => "UI_ROLE_WARRIOR",
			ERole.Wizard => "UI_ROLE_WIZARD",
			ERole.Healer => "UI_ROLE_HEALER",
			ERole.Guard => "UI_ROLE_GUARD",
			ERole.Shield => "UI_ROLE_SHIELD",
			ERole.Controller => "UI_ROLE_CONTROLLER",
			ERole.Support => "UI_ROLE_SUPPORT",
			ERole.CardPlayer => "UI_ROLE_CARD_PLAYER",
			ERole.SwordMan => "UI_ROLE_SWORD_MAN",
			ERole.Mage => "UI_ROLE_MAGE",
			ERole.Alchemist => "UI_ROLE_ALCHEMIST",
			ERole.Elementist => "UI_ROLE_ELEMENTIST",
			_ => "",
		};
		return key.Length > 0;
	}
}
```

注意：本文件引用 `ECardFilterField` / `ECardFilterOp`，这两个枚举在 Task 2 创建。若编译顺序导致暂时失败，可先把枚举放在本文件顶部，Task 2 再移到 `CardCodexQuery.cs`；**推荐直接在 Task 2 先创建枚举文件，再回来补本文件**——执行时按 Task 2 → 再完成本 Task 的 Definitions 亦可。为避免循环依赖，**枚举与 `CardFilterCondition` 放在 `CardCodexQuery.cs`，本 Task 在 Task 2 Step 3 之后提交时一并加入 Definitions。**

调整执行顺序：本 Task 的 Step 2 与 Commit 延后到 Task 2 枚举落地后；本 Task Step 1（csv）可先做。

- [ ] **Step 3: Commit（仅 csv，若先做）**

```powershell
git add Resource/Locale/strings.csv
git commit -m "feat(locale): 增加图鉴卡牌过滤字段与操作翻译键"
```

（`CodexFilterDefinitions.cs` 与枚举一起在 Task 2 末尾提交亦可；二选一，避免空引用编译失败。）

---

### Task 2: CardCodexQuery — 类型与单条件匹配（TDD）

**Files:**
- Create: `Src/mod/global/Ui/CardCodexQuery.cs`
- Create: `Src/mod/global/Def/CodexFilterDefinitions.cs`（若 Task 1 未创建）
- Create: `Tests/kemo_card.Ui.Tests/CardCodexQueryTests.cs`

- [ ] **Step 1: 写失败测试 — 各字段匹配**

创建 `Tests/kemo_card.Ui.Tests/CardCodexQueryTests.cs`：

```csharp
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class CardCodexQueryTests
{
	private static CardDto Card(
		string id = "c1",
		ECardType type = ECardType.Physics,
		int cost = 1,
		ECostType costType = ECostType.Energy,
		int element = (int)EElement.Red,
		ERole role = ERole.Warrior,
		IEnumerable<string>? tags = null,
		bool hideInDex = false,
		string displayNameId = "card.c1.name",
		IEnumerable<string>? skillIds = null) => new()
	{
		Id = id,
		DisplayNameId = displayNameId,
		CardType = type,
		Cost = cost,
		CostType = costType,
		Element = element,
		Role = role,
		Tags = tags?.ToList() ?? [],
		HideInDex = hideInDex,
		SkillRefs = (skillIds ?? []).Select(s => new SkillRefDto { SkillId = s }).ToList(),
	};

	[Test]
	public void MatchesCondition_card_type_equal_and_not_equal()
	{
		var card = Card(type: ECardType.Physics);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.CardType, ECardFilterOp.Equal, nameof(ECardType.Physics), "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.CardType, ECardFilterOp.Equal, nameof(ECardType.Magical), "")), Is.False);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.CardType, ECardFilterOp.NotEqual, nameof(ECardType.Magical), "")), Is.True);
	}

	[Test]
	public void MatchesCondition_cost_comparisons()
	{
		var card = Card(cost: 2);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Cost, ECardFilterOp.Equal, "2", "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Cost, ECardFilterOp.LessOrEqual, "2", "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Cost, ECardFilterOp.LessOrEqual, "1", "")), Is.False);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Cost, ECardFilterOp.GreaterOrEqual, "2", "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Cost, ECardFilterOp.GreaterOrEqual, "3", "")), Is.False);
	}

	[Test]
	public void MatchesCondition_element_contains_and_exact()
	{
		var multi = Card(element: (int)(EElement.Red | EElement.Blue));
		Assert.That(CardCodexQuery.MatchesCondition(multi, new(ECardFilterField.Element, ECardFilterOp.Contains, nameof(EElement.Red), "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(multi, new(ECardFilterField.Element, ECardFilterOp.Exact, nameof(EElement.Red), "")), Is.False);
		var single = Card(element: (int)EElement.Red);
		Assert.That(CardCodexQuery.MatchesCondition(single, new(ECardFilterField.Element, ECardFilterOp.Exact, nameof(EElement.Red), "")), Is.True);
	}

	[Test]
	public void MatchesCondition_tag_contains_and_exact()
	{
		var card = Card(tags: ["attack", "basic"]);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Tag, ECardFilterOp.Contains, "attack", "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Tag, ECardFilterOp.Exact, "attack", "")), Is.False);
		var only = Card(tags: ["attack"]);
		Assert.That(CardCodexQuery.MatchesCondition(only, new(ECardFilterField.Tag, ECardFilterOp.Exact, "attack", "")), Is.True);
	}

	[Test]
	public void MatchesCondition_role_and_cost_type()
	{
		var card = Card(role: ERole.Healer, costType: ECostType.Health);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Role, ECardFilterOp.Equal, nameof(ERole.Healer), "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.Role, ECardFilterOp.NotEqual, nameof(ERole.Warrior), "")), Is.True);
		Assert.That(CardCodexQuery.MatchesCondition(card, new(ECardFilterField.CostType, ECardFilterOp.Equal, nameof(ECostType.Health), "")), Is.True);
	}
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~CardCodexQueryTests"
```

Expected: FAIL（类型不存在）

- [ ] **Step 3: 实现 CardCodexQuery 类型与 MatchesCondition**

创建 `Src/mod/global/Ui/CardCodexQuery.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Global.Ui;

public enum ECardFilterField
{
	CardType,
	Cost,
	Element,
	Role,
	CostType,
	Tag,
}

public enum ECardFilterOp
{
	Equal,
	NotEqual,
	LessOrEqual,
	GreaterOrEqual,
	Contains,
	Exact,
}

public readonly record struct CardFilterCondition(
	ECardFilterField Field,
	ECardFilterOp Op,
	string ValueId,
	string DisplayText);

public static class CardCodexQuery
{
	public const int PageSize = 8;
	public const int MaxCostOption = 10;

	public static IReadOnlyList<ECardFilterOp> OpsForField(ECardFilterField field) => field switch
	{
		ECardFilterField.Cost => [ECardFilterOp.LessOrEqual, ECardFilterOp.Equal, ECardFilterOp.GreaterOrEqual],
		ECardFilterField.CardType or ECardFilterField.Role or ECardFilterField.CostType =>
			[ECardFilterOp.Equal, ECardFilterOp.NotEqual],
		ECardFilterField.Element or ECardFilterField.Tag =>
			[ECardFilterOp.Contains, ECardFilterOp.Exact],
		_ => [ECardFilterOp.Equal],
	};

	public static bool MatchesCondition(CardDto card, CardFilterCondition condition)
	{
		return condition.Field switch
		{
			ECardFilterField.CardType => MatchEnum(card.CardType, condition),
			ECardFilterField.Role => MatchEnum(card.Role, condition),
			ECardFilterField.CostType => MatchEnum(card.CostType, condition),
			ECardFilterField.Cost => MatchCost(card.Cost, condition),
			ECardFilterField.Element => MatchElement(card.Element, condition),
			ECardFilterField.Tag => MatchTag(card.Tags, condition),
			_ => false,
		};
	}

	private static bool MatchEnum<T>(T actual, CardFilterCondition condition) where T : struct, Enum
	{
		if (!Enum.TryParse<T>(condition.ValueId, ignoreCase: false, out var expected))
		{
			return false;
		}

		return condition.Op switch
		{
			ECardFilterOp.Equal => EqualityComparer<T>.Default.Equals(actual, expected),
			ECardFilterOp.NotEqual => !EqualityComparer<T>.Default.Equals(actual, expected),
			_ => false,
		};
	}

	private static bool MatchCost(int cost, CardFilterCondition condition)
	{
		if (!int.TryParse(condition.ValueId, out var expected))
		{
			return false;
		}

		return condition.Op switch
		{
			ECardFilterOp.Equal => cost == expected,
			ECardFilterOp.LessOrEqual => cost <= expected,
			ECardFilterOp.GreaterOrEqual => cost >= expected,
			_ => false,
		};
	}

	private static bool MatchElement(int flags, CardFilterCondition condition)
	{
		if (!Enum.TryParse<EElement>(condition.ValueId, out var element) || element == EElement.None)
		{
			return false;
		}

		var bit = (int)element;
		return condition.Op switch
		{
			ECardFilterOp.Contains => (flags & bit) != 0,
			ECardFilterOp.Exact => flags == bit,
			_ => false,
		};
	}

	private static bool MatchTag(IReadOnlyList<string> tags, CardFilterCondition condition)
	{
		var value = condition.ValueId;
		return condition.Op switch
		{
			ECardFilterOp.Contains => tags.Any(t => string.Equals(t, value, StringComparison.Ordinal)),
			ECardFilterOp.Exact => tags.Count == 1 && string.Equals(tags[0], value, StringComparison.Ordinal),
			_ => false,
		};
	}
}
```

同时创建 Task 1 的 `CodexFilterDefinitions.cs`（完整内容见 Task 1 Step 2）。

- [ ] **Step 4: 运行测试确认通过**

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~CardCodexQueryTests"
```

Expected: PASS

- [ ] **Step 5: Commit**

```powershell
git add Src/mod/global/Ui/CardCodexQuery.cs Src/mod/global/Def/CodexFilterDefinitions.cs Tests/kemo_card.Ui.Tests/CardCodexQueryTests.cs Resource/Locale/strings.csv
git commit -m "feat(ui): 添加图鉴卡牌过滤匹配纯逻辑与本地化定义"
```

---

### Task 3: Filter / 文本搜索 / 分页切片 / 标签收集

**Files:**
- Modify: `Src/mod/global/Ui/CardCodexQuery.cs`
- Modify: `Tests/kemo_card.Ui.Tests/CardCodexQueryTests.cs`

- [ ] **Step 1: 写失败测试**

在 `CardCodexQueryTests` 追加：

```csharp
	[Test]
	public void Filter_excludes_hide_in_dex_and_ands_conditions()
	{
		var cards = new Dictionary<string, CardDto>
		{
			["a"] = Card(id: "a", type: ECardType.Physics, cost: 1),
			["b"] = Card(id: "b", type: ECardType.Physics, cost: 3),
			["c"] = Card(id: "c", type: ECardType.Magical, cost: 1),
			["h"] = Card(id: "h", type: ECardType.Physics, cost: 1, hideInDex: true),
		};

		var conditions = new[]
		{
			new CardFilterCondition(ECardFilterField.CardType, ECardFilterOp.Equal, nameof(ECardType.Physics), ""),
			new CardFilterCondition(ECardFilterField.Cost, ECardFilterOp.LessOrEqual, "2", ""),
		};

		var result = CardCodexQuery.Filter(cards.Values, conditions, textQuery: "", _ => "", _ => null);
		Assert.That(result.Select(c => c.Id), Is.EqualTo(new[] { "a" }));
	}

	[Test]
	public void Filter_text_matches_name_and_skill_desc()
	{
		var cards = new[]
		{
			Card(id: "n", displayNameId: "card.fire.name", skillIds: ["s1"]),
			Card(id: "d", displayNameId: "card.ice.name", skillIds: ["s2"]),
			Card(id: "x", displayNameId: "card.rock.name", skillIds: ["s3"]),
		};

		string Tr(string key) => key switch
		{
			"card.fire.name" => "火焰打击",
			"card.ice.name" => "寒冰护盾",
			"skill.s2.desc" => "造成火焰伤害",
			_ => key,
		};

		SkillDto? GetSkill(string id) => id switch
		{
			"s1" => new SkillDto { Id = "s1", DescId = "skill.s1.desc" },
			"s2" => new SkillDto { Id = "s2", DescId = "skill.s2.desc" },
			_ => null,
		};

		var byName = CardCodexQuery.Filter(cards, [], "火焰", Tr, GetSkill);
		Assert.That(byName.Select(c => c.Id), Is.EqualTo(new[] { "n", "d" }).AsCollection);
		// "火焰" 命中卡名「火焰打击」与技能描述「造成火焰伤害」

		var empty = CardCodexQuery.Filter(cards, [], "  ", Tr, GetSkill);
		Assert.That(empty.Count, Is.EqualTo(3));
	}

	[Test]
	public void SlicePage_and_collect_tags()
	{
		var cards = Enumerable.Range(0, 10)
			.Select(i => Card(id: $"c{i:D2}", tags: i % 2 == 0 ? ["even", "shared"] : ["odd"]))
			.ToList();

		var page0 = CardCodexQuery.SlicePage(cards, page: 0, pageSize: 8);
		Assert.That(page0.Count, Is.EqualTo(8));
		Assert.That(page0[0].Id, Is.EqualTo("c00"));

		var page1 = CardCodexQuery.SlicePage(cards, page: 1, pageSize: 8);
		Assert.That(page1.Count, Is.EqualTo(2));

		Assert.That(CardCodexQuery.TotalPages(10, 8), Is.EqualTo(2));
		Assert.That(CardCodexQuery.TotalPages(0, 8), Is.EqualTo(0));

		var tags = CardCodexQuery.CollectTags(cards);
		Assert.That(tags, Is.EqualTo(new[] { "even", "odd", "shared" }));
	}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~CardCodexQueryTests"
```

Expected: FAIL（`Filter` / `SlicePage` 等缺失）

- [ ] **Step 3: 实现 Filter / MatchesText / SlicePage / TotalPages / CollectTags**

在 `CardCodexQuery` 类中追加：

```csharp
	public static IReadOnlyList<CardDto> Filter(
		IEnumerable<CardDto> cards,
		IReadOnlyList<CardFilterCondition> conditions,
		string textQuery,
		Func<string, string> translate,
		Func<string, SkillDto?> tryGetSkill)
	{
		var query = textQuery?.Trim() ?? "";
		return cards
			.Where(c => !c.HideInDex)
			.Where(c => conditions.All(cond => MatchesCondition(c, cond)))
			.Where(c => string.IsNullOrEmpty(query) || MatchesText(c, query, translate, tryGetSkill))
			.OrderBy(c => c.Id, StringComparer.Ordinal)
			.ToList();
	}

	public static bool MatchesText(
		CardDto card,
		string query,
		Func<string, string> translate,
		Func<string, SkillDto?> tryGetSkill)
	{
		if (ContainsIgnoreCase(translate(card.DisplayNameId), query)
			|| ContainsIgnoreCase(card.DisplayNameId, query))
		{
			return true;
		}

		foreach (var skillRef in card.SkillRefs)
		{
			var skill = tryGetSkill(skillRef.SkillId);
			if (skill == null || string.IsNullOrEmpty(skill.DescId))
			{
				continue;
			}

			if (ContainsIgnoreCase(translate(skill.DescId), query)
				|| ContainsIgnoreCase(skill.DescId, query))
			{
				return true;
			}
		}

		return false;
	}

	public static IReadOnlyList<CardDto> SlicePage(IReadOnlyList<CardDto> cards, int page, int pageSize)
	{
		if (cards.Count == 0 || pageSize <= 0 || page < 0)
		{
			return Array.Empty<CardDto>();
		}

		var start = page * pageSize;
		if (start >= cards.Count)
		{
			return Array.Empty<CardDto>();
		}

		var count = Math.Min(pageSize, cards.Count - start);
		var slice = new CardDto[count];
		for (var i = 0; i < count; i++)
		{
			slice[i] = cards[start + i];
		}

		return slice;
	}

	public static int TotalPages(int itemCount, int pageSize)
	{
		if (itemCount <= 0 || pageSize <= 0)
		{
			return 0;
		}

		return (itemCount + pageSize - 1) / pageSize;
	}

	public static IReadOnlyList<string> CollectTags(IEnumerable<CardDto> cards) =>
		cards
			.Where(c => !c.HideInDex)
			.SelectMany(c => c.Tags)
			.Where(t => !string.IsNullOrWhiteSpace(t))
			.Distinct(StringComparer.Ordinal)
			.OrderBy(t => t, StringComparer.Ordinal)
			.ToList();

	private static bool ContainsIgnoreCase(string haystack, string needle) =>
		!string.IsNullOrEmpty(haystack)
		&& haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
```

- [ ] **Step 4: 运行测试确认通过**

同 Step 2，Expected: PASS

- [ ] **Step 5: Commit**

```powershell
git add Src/mod/global/Ui/CardCodexQuery.cs Tests/kemo_card.Ui.Tests/CardCodexQueryTests.cs
git commit -m "feat(ui): 图鉴过滤支持多条件 AND、文本搜索与分页切片"
```

---

### Task 4: CodexDlg 场景 Export 绑定

**Files:**
- Modify: `Src/mod/global/Ui/CodexDlg.cs`
- Modify: `Src/mod/global/Ui/CodexDlg.tscn`

- [ ] **Step 1: 在 CodexDlg.cs 增加 Export 字段（先不写业务）**

将 `CodexDlg.cs` 改为：

```csharp
using Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global.Ui.Comp;

namespace KemoCard.Mod.Global.Ui;

public record struct CodexDlgPayload;

public partial class CodexDlg : BaseDlg
{
	[Export] private OptionButton? _obField;
	[Export] private OptionButton? _obOp;
	[Export] private OptionButton? _obVal;
	[Export] private ItemList? _itemListConditions;
	[Export] private Button? _btnAdd;
	[Export] private LineEdit? _iptTxtFilter;
	[Export] private GridContainer? _gridCardList;
	[Export] private BasePager? _pager;

	public override string UIId => GlobalUiIds.Codex;
	public override string UIDir => "Src/mod/global/Ui";

	protected override void OnOpen()
	{
	}

	protected override void UpdateView()
	{
	}
}
```

- [ ] **Step 2: 在 CodexDlg.tscn 根节点绑定 node_paths**

编辑 `Src/mod/global/Ui/CodexDlg.tscn` 根节点，使类似：

```
[node name="CodexDlg" type="Control" ... node_paths=PackedStringArray("_obField", "_obOp", "_obVal", "_itemListConditions", "_btnAdd", "_iptTxtFilter", "_gridCardList", "_pager")]
...
_obField = NodePath("TabContainer/UI_CARD/OBCardTypeSelector")
_obOp = NodePath("TabContainer/UI_CARD/OBCardOperateSelector")
_obVal = NodePath("TabContainer/UI_CARD/OBCardValSelector")
_itemListConditions = NodePath("TabContainer/UI_CARD/ItemListConditions")
_btnAdd = NodePath("TabContainer/UI_CARD/BAdd")
_iptTxtFilter = NodePath("TabContainer/UI_CARD/IptTxtFilter")
_gridCardList = NodePath("TabContainer/UI_CARD/Control/GridContainerCardList")
_pager = NodePath("TabContainer/UI_CARD/BasePager")
script = ExtResource("1_x5fxm")
```

（可用 Godot 编辑器勾选 Export 赋值；保证路径与现有节点名一致。）

- [ ] **Step 3: Commit**

```powershell
git add Src/mod/global/Ui/CodexDlg.cs Src/mod/global/Ui/CodexDlg.tscn
git commit -m "chore(ui): CodexDlg 绑定卡牌过滤与列表控件 Export"
```

---

### Task 5: CodexDlg — 下拉联动与条件增删

**Files:**
- Modify: `Src/mod/global/Ui/CodexDlg.cs`

- [ ] **Step 1: 实现字段/操作/值填充与条件列表逻辑**

在 `CodexDlg` 中追加状态与方法（完整类逻辑骨架；Task 6 再接列表刷新调用点）：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Ui.Comp;
using KemoCard.Mod;

// ... existing exports ...

	private readonly List<CardFilterCondition> _conditions = [];
	private IReadOnlyList<CardDto> _filteredCards = [];
	private List<BaseCardItem> _cardSlots = [];
	private bool _eventsBound;

	protected override void InitEvent()
	{
		if (_eventsBound)
		{
			return;
		}

		_eventsBound = true;

		if (_obField != null)
		{
			_obField.ItemSelected += OnFieldSelected;
		}

		if (_btnAdd != null)
		{
			OnClicks(_btnAdd, OnAddPressed);
		}

		if (_itemListConditions != null)
		{
			_itemListConditions.ItemClicked += OnConditionItemClicked;
		}

		if (_iptTxtFilter != null)
		{
			_iptTxtFilter.TextSubmitted += OnTextSubmitted;
			_iptTxtFilter.FocusExited += OnTextFocusExited;
		}

		if (_pager != null)
		{
			_pager.OnPageChanged = OnPagerPageChanged;
		}
	}

	protected override void OnOpen()
	{
		CacheCardSlots();
		PopulateFieldOptions();
		RebuildOpAndValOptions();
		RefreshFilteredList(resetPage: true);
	}

	protected override void UpdateView()
	{
		RefreshFilteredList(resetPage: false);
	}

	private void CacheCardSlots()
	{
		_cardSlots = [];
		if (_gridCardList == null)
		{
			return;
		}

		foreach (var child in _gridCardList.GetChildren())
		{
			if (child is BaseCardItem item)
			{
				_cardSlots.Add(item);
			}
		}
	}

	private void PopulateFieldOptions()
	{
		if (_obField == null)
		{
			return;
		}

		_obField.Clear();
		foreach (ECardFilterField field in Enum.GetValues<ECardFilterField>())
		{
			_obField.AddItem(Localization.Tr(CodexFilterDefinitions.GetFieldLocaleKey(field)));
			_obField.SetItemMetadata(_obField.ItemCount - 1, (int)field);
		}

		_obField.Selected = 0;
	}

	private void OnFieldSelected(long _)
	{
		RebuildOpAndValOptions();
	}

	private void RebuildOpAndValOptions()
	{
		var field = GetSelectedField();
		PopulateOpOptions(field);
		PopulateValOptions(field);
	}

	private ECardFilterField GetSelectedField()
	{
		if (_obField == null || _obField.Selected < 0)
		{
			return ECardFilterField.CardType;
		}

		return (ECardFilterField)(int)_obField.GetItemMetadata(_obField.Selected);
	}

	private void PopulateOpOptions(ECardFilterField field)
	{
		if (_obOp == null)
		{
			return;
		}

		_obOp.Clear();
		foreach (var op in CardCodexQuery.OpsForField(field))
		{
			_obOp.AddItem(Localization.Tr(CodexFilterDefinitions.GetOpLocaleKey(op)));
			_obOp.SetItemMetadata(_obOp.ItemCount - 1, (int)op);
		}

		_obOp.Selected = 0;
	}

	private void PopulateValOptions(ECardFilterField field)
	{
		if (_obVal == null)
		{
			return;
		}

		_obVal.Clear();
		switch (field)
		{
			case ECardFilterField.CardType:
				foreach (ECardType v in Enum.GetValues<ECardType>())
				{
					var text = CardUiDefinitions.TryGetCardTypeLocaleKey(v, out var key)
						? Localization.Tr(key)
						: v.ToString();
					_obVal.AddItem(text);
					_obVal.SetItemMetadata(_obVal.ItemCount - 1, v.ToString());
				}
				break;
			case ECardFilterField.Cost:
				for (var i = 0; i <= CardCodexQuery.MaxCostOption; i++)
				{
					_obVal.AddItem(i.ToString());
					_obVal.SetItemMetadata(_obVal.ItemCount - 1, i.ToString());
				}
				break;
			case ECardFilterField.Element:
				foreach (EElement v in Enum.GetValues<EElement>())
				{
					if (v == EElement.None || !CodexFilterDefinitions.TryGetElementLocaleKey(v, out var key))
					{
						continue;
					}

					_obVal.AddItem(Localization.Tr(key));
					_obVal.SetItemMetadata(_obVal.ItemCount - 1, v.ToString());
				}
				break;
			case ECardFilterField.Role:
				foreach (ERole v in Enum.GetValues<ERole>())
				{
					var text = CodexFilterDefinitions.TryGetRoleLocaleKey(v, out var key)
						? Localization.Tr(key)
						: v.ToString();
					_obVal.AddItem(text);
					_obVal.SetItemMetadata(_obVal.ItemCount - 1, v.ToString());
				}
				break;
			case ECardFilterField.CostType:
				foreach (ECostType v in Enum.GetValues<ECostType>())
				{
					var text = CodexFilterDefinitions.TryGetCostTypeLocaleKey(v, out var key)
						? Localization.Tr(key)
						: v.ToString();
					_obVal.AddItem(text);
					_obVal.SetItemMetadata(_obVal.ItemCount - 1, v.ToString());
				}
				break;
			case ECardFilterField.Tag:
				var store = AppRoot.Services.ContentModPipeline.Registry.Store;
				foreach (var tag in CardCodexQuery.CollectTags(store.Cards.Values))
				{
					_obVal.AddItem(tag);
					_obVal.SetItemMetadata(_obVal.ItemCount - 1, tag);
				}
				break;
		}

		_obVal.Selected = _obVal.ItemCount > 0 ? 0 : -1;
	}

	private void OnAddPressed()
	{
		if (_obOp == null || _obVal == null || _obVal.Selected < 0 || _obOp.Selected < 0)
		{
			return;
		}

		var field = GetSelectedField();
		var op = (ECardFilterOp)(int)_obOp.GetItemMetadata(_obOp.Selected);
		var valueId = _obVal.GetItemMetadata(_obVal.Selected).AsString();
		if (string.IsNullOrEmpty(valueId))
		{
			return;
		}

		var display = $"{Localization.Tr(CodexFilterDefinitions.GetFieldLocaleKey(field))} {Localization.Tr(CodexFilterDefinitions.GetOpLocaleKey(op))} {_obVal.GetItemText(_obVal.Selected)}";
		_conditions.Add(new CardFilterCondition(field, op, valueId, display));
		_itemListConditions?.AddItem(display);
		RefreshFilteredList(resetPage: true);
	}

	private void OnConditionItemClicked(long index, Vector2 _, long mouseButtonIndex)
	{
		if (mouseButtonIndex != (long)MouseButton.Left)
		{
			return;
		}

		var i = (int)index;
		if (i < 0 || i >= _conditions.Count)
		{
			return;
		}

		_conditions.RemoveAt(i);
		_itemListConditions?.RemoveItem(i);
		RefreshFilteredList(resetPage: true);
	}

	private void OnTextSubmitted(string _) => RefreshFilteredList(resetPage: true);

	private void OnTextFocusExited() => RefreshFilteredList(resetPage: true);

	private void OnPagerPageChanged(int _) => FillCurrentPage();

	// RefreshFilteredList / FillCurrentPage 在 Task 6 实现；此处先放空实现以免编译失败：
	private void RefreshFilteredList(bool resetPage) { }
	private void FillCurrentPage() { }
```

- [ ] **Step 2: 编译检查**

```powershell
dotnet build kemo_card.csproj
```

Expected: 成功（允许 Godot 相关警告）

- [ ] **Step 3: Commit**

```powershell
git add Src/mod/global/Ui/CodexDlg.cs
git commit -m "feat(ui): CodexDlg 实现过滤下拉联动与条件增删"
```

---

### Task 6: CodexDlg — 过滤结果填充与分页

**Files:**
- Modify: `Src/mod/global/Ui/CodexDlg.cs`

- [ ] **Step 1: 实现 RefreshFilteredList / FillCurrentPage**

将 Task 5 中的空方法替换为：

```csharp
	private void RefreshFilteredList(bool resetPage)
	{
		var store = AppRoot.Services.ContentModPipeline.Registry.Store;
		var text = _iptTxtFilter?.Text ?? "";
		_filteredCards = CardCodexQuery.Filter(
			store.Cards.Values,
			_conditions,
			text,
			Localization.Tr,
			id => store.TryGetSkill(id, out var skill) ? skill : null);

		if (_pager != null)
		{
			_pager.TotalPages = CardCodexQuery.TotalPages(_filteredCards.Count, CardCodexQuery.PageSize);
			if (resetPage)
			{
				_pager.SetPage(0);
			}
			else
			{
				// TotalPages setter 可能已钳制页码；仍刷新当前页
				FillCurrentPage();
			}
		}
		else
		{
			FillCurrentPage();
		}

		// SetPage(0) 会经 OnPageChanged 调 FillCurrentPage；若页码未变则需手动填
		if (resetPage && _pager != null && _pager.CurrentPage == 0)
		{
			FillCurrentPage();
		}
	}

	private void FillCurrentPage()
	{
		var page = _pager?.CurrentPage ?? 0;
		var slice = CardCodexQuery.SlicePage(_filteredCards, page, CardCodexQuery.PageSize);
		for (var i = 0; i < _cardSlots.Count; i++)
		{
			_cardSlots[i].SetData(i < slice.Count ? slice[i] : null);
		}
	}
```

注意：`BasePager.SetPage` 在页码不变时不会触发 `OnPageChanged`。因此 `resetPage: true` 且已在第 0 页时必须手动 `FillCurrentPage()`（上面已处理）。也可统一在 `RefreshFilteredList` 末尾总是调用一次 `FillCurrentPage()`，并让 `OnPagerPageChanged` 只在用户翻页时调用——**推荐简化为：**

```csharp
	private void RefreshFilteredList(bool resetPage)
	{
		var store = AppRoot.Services.ContentModPipeline.Registry.Store;
		var text = _iptTxtFilter?.Text ?? "";
		_filteredCards = CardCodexQuery.Filter(
			store.Cards.Values,
			_conditions,
			text,
			Localization.Tr,
			id => store.TryGetSkill(id, out var skill) ? skill : null);

		if (_pager != null)
		{
			var pages = CardCodexQuery.TotalPages(_filteredCards.Count, CardCodexQuery.PageSize);
			_pager.TotalPages = pages;
			if (resetPage)
			{
				_pager.SetPage(0);
			}
		}

		FillCurrentPage();
	}

	private void OnPagerPageChanged(int _) => FillCurrentPage();
```

这样过滤变更与翻页都安全。`SetPage` 触发的 `OnPageChanged` 会导致 `FillCurrentPage` 调用两次，可接受。

- [ ] **Step 2: 编译 + 单测回归**

```powershell
dotnet build kemo_card.csproj
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~CardCodexQueryTests"
```

Expected: 均通过

- [ ] **Step 3: 手动验收（Godot 运行）**

按规格第 8 节：

1. 打开图鉴，卡牌 Tab 显示非 `HideInDex` 卡，分页正确  
2. 添加条件 → ItemList 出现且列表刷新  
3. 点击条件项 → 移除并刷新  
4. 多条件 AND；文本回车/失焦刷新  
5. 标签「包含」可用  
6. 翻页保留过滤；改过滤回第 1 页  
7. 无硬编码用户可见文案  

- [ ] **Step 4: Commit**

```powershell
git add Src/mod/global/Ui/CodexDlg.cs
git commit -m "feat(ui): CodexDlg 卡牌图鉴过滤结果分页展示"
```

---

## Spec 覆盖自检

| 规格项 | 任务 |
|--------|------|
| 字段：类型/费用/元素/角色/费用类型/标签 | Task 2–5 |
| 操作符表 | Task 2 `OpsForField` + Task 5 下拉 |
| 条件 AND、点击移除、添加即刷新 | Task 5–6 |
| 文本搜索卡名+技能 DescId | Task 3–6 |
| HideInDex 排除 | Task 3 |
| PageSize=8、BasePager、BaseCardItem | Task 4–6 |
| 本地化 | Task 1–2、5 |
| 纯逻辑单测 | Task 2–3 |
| 不做 VirtualList / race / 解锁灰显 | 无对应任务 |

无 TBD/占位步骤；类型名在各 Task 间一致（`ECardFilterField` / `ECardFilterOp` / `CardFilterCondition` / `CardCodexQuery`）。
