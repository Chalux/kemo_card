# 卡牌详情对话框 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现从 `BaseCardItem` 点击打开的 `CardDetailsDlg`：展示卡面/卡名/Mod/画师/技能描述，描述内 BBCode 关键词悬停 tip，详情页内卡牌关闭点击以防套娃。

**Architecture:** `CardDetailsDlg : BaseDlg` 按 payload 查卡并绑定已有场景；`CardDescBuilder` + `KeywordMetaParser` 提供可单测的纯逻辑；`BaseCardItem` 用 `ECardClickAction` 控制是否 `OpenAsync` 详情；`ModScriptCatalog` 缓存 `modId → displayNameKey`。

**Tech Stack:** Godot 4.6 Mono / C# / NUnit

**Spec:** `Doc/superpowers/specs/2026-07-13-card-details-dlg-design.md`

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `Src/frame/content/definitions/CardDto.cs` | 新增 `ArtistNameId` |
| `Src/frame/scripting/ModScriptCatalog.cs` | Rebuild 时缓存 displayNameKey |
| `Src/mod/global/Def/ECardClickAction.cs` | `None` / `OpenDetails` |
| `Src/mod/global/Ui/CardDescBuilder.cs` | 技能描述拼接 + `kw:` meta 解析 |
| `Src/mod/global/Ui/CardDetailsDlg.cs` | 对话框逻辑 |
| `Src/mod/global/Ui/CardDetailsDlg.tscn` | Export 绑定；详情卡 `ClickAction=None` |
| `Src/mod/global/Ui/Comp/BaseCardItem.cs` | 点击打开详情 |
| `Src/mod/global/Ui/GlobalUiIds.cs` | `CardDetails` 常量 |
| `Src/mod/global/GlobalMod.cs` | 注册 Dialog |
| `Config/mods/base-game/content/cards/strike.json` | `artistNameId` |
| `Config/mods/base-game/content/translations/strings.csv` | 卡名/画师/技能描述（含 url meta） |
| `Tests/kemo_card.Ui.Tests/CardDescBuilderTests.cs` | 描述与 meta 单测 |
| `Tests/kemo_card.Ui.Tests/ModScriptCatalogTests.cs` | displayName 缓存单测 |

依赖（只读）：`KeywordTipService`、`KeywordTipRequest`、`BaseDlg`、`BaseDlgComp`、`AppRoot`、`UIManager`、`Localization`。

---

### Task 1: CardDto.ArtistNameId

**Files:**
- Modify: `Src/frame/content/definitions/CardDto.cs`
- Modify: `Tests/kemo_card.Ui.Tests/ContentDefinitionTests.cs`（或新增断言到现有反序列化测试）

- [ ] **Step 1: 在 CardDto 增加字段**

在 `DisplayNameId` 附近加入：

```csharp
[JsonPropertyName("artistNameId")]
public string ArtistNameId { get; init; } = "";
```

- [ ] **Step 2: 扩展 ContentDefinitionTests 的 round-trip**

在 `CardDto_round_trips_through_json` 中为 `original` 设置 `ArtistNameId = "card.strike.artist"`，并追加断言：

```csharp
Assert.That(restored.ArtistNameId, Is.EqualTo("card.strike.artist"));
Assert.That(json, Does.Contain("artistNameId"));
```

- [ ] **Step 3: 运行测试**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "CardDto_round_trips_through_json" -v n`

Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add Src/frame/content/definitions/CardDto.cs Tests/kemo_card.Ui.Tests/ContentDefinitionTests.cs
git commit -m "feat(content): CardDto 增加 artistNameId"
```

---

### Task 2: ModScriptCatalog 缓存 displayNameKey

**Files:**
- Modify: `Src/frame/scripting/ModScriptCatalog.cs`
- Modify: `Tests/kemo_card.Ui.Tests/ModScriptCatalogTests.cs`

- [ ] **Step 1: 写失败测试**

在 `ModScriptCatalogTests.cs` 追加：

```csharp
[Test]
public void TryGetDisplayNameKey_returns_manifest_display_name()
{
	var catalog = new ModScriptCatalog();
	catalog.Rebuild(
	[
		new DiscoveredModEntry(
			Path.Combine(Path.GetTempPath(), "unused"),
			new ContentModManifestDto
			{
				ModId = "base.game",
				DisplayName = "MOD_BASE_GAME_DISPLAY_NAME",
				ContentRoot = "content",
			}),
	]);

	Assert.That(catalog.TryGetDisplayNameKey("base.game", out var key), Is.True);
	Assert.That(key, Is.EqualTo("MOD_BASE_GAME_DISPLAY_NAME"));
}

[Test]
public void TryGetDisplayNameKey_unknown_returns_false()
{
	var catalog = new ModScriptCatalog();
	Assert.That(catalog.TryGetDisplayNameKey("missing", out _), Is.False);
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "TryGetDisplayNameKey" -v n`

Expected: FAIL（方法不存在）

- [ ] **Step 3: 实现缓存**

将 `_byModId` 扩展为包含 `DisplayNameKey`（或并列字典）。`Rebuild` 写入 `entry.Manifest.DisplayName`。新增：

```csharp
public bool TryGetDisplayNameKey(string modId, out string displayNameKey)
{
	if (_byModId.TryGetValue(modId, out var entry))
	{
		displayNameKey = entry.DisplayNameKey;
		return true;
	}

	displayNameKey = null!;
	return false;
}
```

元组示例：`(string FolderPath, string ContentRoot, string DisplayNameKey)`。

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "TryGetDisplayNameKey" -v n`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Src/frame/scripting/ModScriptCatalog.cs Tests/kemo_card.Ui.Tests/ModScriptCatalogTests.cs
git commit -m "feat(content): ModScriptCatalog 缓存 Mod 显示名键"
```

---

### Task 3: CardDescBuilder + KeywordMetaParser（TDD）

**Files:**
- Create: `Src/mod/global/Ui/CardDescBuilder.cs`
- Create: `Tests/kemo_card.Ui.Tests/CardDescBuilderTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class CardDescBuilderTests
{
	[Test]
	public void Build_joins_skill_descs_with_newline()
	{
		var card = new CardDto
		{
			SkillRefs =
			[
				new SkillRefDto { SkillId = "s1" },
				new SkillRefDto { SkillId = "s2" },
			],
		};

		var text = CardDescBuilder.Build(
			card,
			id => id switch
			{
				"s1" => new SkillDto { Id = "s1", DescId = "d1" },
				"s2" => new SkillDto { Id = "s2", DescId = "d2" },
				_ => null,
			},
			key => key switch
			{
				"d1" => "第一段",
				"d2" => "第二段",
				_ => key,
			});

		Assert.That(text, Is.EqualTo("第一段\n第二段"));
	}

	[Test]
	public void Build_skips_missing_skill_or_empty_desc()
	{
		var card = new CardDto
		{
			SkillRefs =
			[
				new SkillRefDto { SkillId = "missing" },
				new SkillRefDto { SkillId = "empty" },
				new SkillRefDto { SkillId = "ok" },
			],
		};

		var text = CardDescBuilder.Build(
			card,
			id => id switch
			{
				"empty" => new SkillDto { Id = "empty", DescId = "" },
				"ok" => new SkillDto { Id = "ok", DescId = "d" },
				_ => null,
			},
			key => key == "d" ? "有效" : key);

		Assert.That(text, Is.EqualTo("有效"));
	}

	[Test]
	public void TryParseKeywordMeta_parses_kw_prefix()
	{
		Assert.That(CardDescBuilder.TryParseKeywordMeta("kw:exhaust", out var id), Is.True);
		Assert.That(id, Is.EqualTo("exhaust"));
	}

	[Test]
	public void TryParseKeywordMeta_rejects_invalid()
	{
		Assert.That(CardDescBuilder.TryParseKeywordMeta("exhaust", out _), Is.False);
		Assert.That(CardDescBuilder.TryParseKeywordMeta("kw:", out _), Is.False);
		Assert.That(CardDescBuilder.TryParseKeywordMeta("", out _), Is.False);
		Assert.That(CardDescBuilder.TryParseKeywordMeta(null, out _), Is.False);
	}
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "CardDescBuilderTests" -v n`

Expected: FAIL

- [ ] **Step 3: 实现 CardDescBuilder.cs**

```csharp
using System.Text;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Global.Ui;

public static class CardDescBuilder
{
	private const string KeywordMetaPrefix = "kw:";

	public static string Build(
		CardDto card,
		Func<string, SkillDto?> resolveSkill,
		Func<string, string> translate)
	{
		ArgumentNullException.ThrowIfNull(card);
		ArgumentNullException.ThrowIfNull(resolveSkill);
		ArgumentNullException.ThrowIfNull(translate);

		var sb = new StringBuilder();
		foreach (var skillRef in card.SkillRefs)
		{
			if (string.IsNullOrWhiteSpace(skillRef.SkillId))
			{
				continue;
			}

			var skill = resolveSkill(skillRef.SkillId);
			if (skill == null || string.IsNullOrEmpty(skill.DescId))
			{
				continue;
			}

			var part = translate(skill.DescId);
			if (string.IsNullOrEmpty(part))
			{
				continue;
			}

			if (sb.Length > 0)
			{
				sb.Append('\n');
			}

			sb.Append(part);
		}

		return sb.ToString();
	}

	public static bool TryParseKeywordMeta(string? meta, out string keywordId)
	{
		keywordId = "";
		if (string.IsNullOrWhiteSpace(meta) || !meta.StartsWith(KeywordMetaPrefix, StringComparison.Ordinal))
		{
			return false;
		}

		var id = meta[KeywordMetaPrefix.Length..].Trim();
		if (string.IsNullOrEmpty(id))
		{
			return false;
		}

		keywordId = id;
		return true;
	}
}
```

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "CardDescBuilderTests" -v n`

Expected: PASS

- [ ] **Step 5: 格式化并 Commit**

对该 `.cs` 文件执行项目格式化（如 `dotnet format` 针对改动文件），然后：

```bash
git add Src/mod/global/Ui/CardDescBuilder.cs Tests/kemo_card.Ui.Tests/CardDescBuilderTests.cs
git commit -m "feat(ui): 卡牌描述拼接与 kw meta 解析"
```

---

### Task 4: UI 注册（GlobalUiIds + GlobalMod）

**Files:**
- Modify: `Src/mod/global/Ui/GlobalUiIds.cs`
- Modify: `Src/mod/global/GlobalMod.cs`

- [ ] **Step 1: 增加 UI Id**

```csharp
public const string CardDetails = "CardDetailsDlg";
```

- [ ] **Step 2: 注册 Dialog**

在 `GetUIRegistrations` 中 Codex 旁追加：

```csharp
yield return UIRegistration.Dialog(GlobalUiIds.CardDetails, "Src/mod/global/Ui");
```

- [ ] **Step 3: Commit**

```bash
git add Src/mod/global/Ui/GlobalUiIds.cs Src/mod/global/GlobalMod.cs
git commit -m "feat(ui): 注册 CardDetailsDlg"
```

---

### Task 5: CardDetailsDlg 逻辑 + 场景绑定

**Files:**
- Modify: `Src/mod/global/Ui/CardDetailsDlg.cs`
- Modify: `Src/mod/global/Ui/CardDetailsDlg.tscn`

- [ ] **Step 1: 实现 CardDetailsDlg.cs**

要点（完整实现时按此结构）：

```csharp
using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Ui.Comp;
using KemoCard.Mod.Global.Ui.Tip;

namespace KemoCard.Mod.Global.Ui;

public record struct CardDetailsDlgPayload
{
	public string CardId { get; init; }
	public int? DisplayValue { get; init; }
}

public partial class CardDetailsDlg : BaseDlg
{
	[Export] private BaseCardItem? _cardItem;
	[Export] private Label? _txtCardName;
	[Export] private Label? _txtModName;
	[Export] private Label? _txtArtistName;
	[Export] private RichTextLabel? _rtCardDesc;

	private bool _eventsBound;

	public override string UIId => GlobalUiIds.CardDetails;
	public override string UIDir => "Src/mod/global/Ui";

	protected override void InitEvent()
	{
		if (_eventsBound)
		{
			return;
		}

		_eventsBound = true;
		if (_cardItem != null)
		{
			_cardItem.ClickAction = ECardClickAction.None;
		}

		if (_rtCardDesc != null)
		{
			_rtCardDesc.MetaHoverStarted += OnMetaHoverStarted;
			_rtCardDesc.MetaHoverEnded += OnMetaHoverEnded;
		}
	}

	protected override void OnOpen() => RefreshFromPayload();

	protected override void UpdateView() => RefreshFromPayload();

	protected override void OnClose()
	{
		HideKeywordTips();
	}

	private void RefreshFromPayload()
	{
		var payload = GetTypedPayload<CardDetailsDlgPayload>();
		if (string.IsNullOrWhiteSpace(payload.CardId))
		{
			GD.PushWarning("CardDetailsDlg: CardId 为空。");
			Close();
			return;
		}

		var store = AppRoot.Services.ContentModPipeline.Registry.Store;
		if (!store.TryGetCard(payload.CardId, out var card))
		{
			GD.PushWarning($"CardDetailsDlg: 未找到卡牌 {payload.CardId}。");
			Close();
			return;
		}

		BindCard(card, payload.DisplayValue);
	}

	private void BindCard(CardDto card, int? displayValue)
	{
		if (_cardItem != null)
		{
			_cardItem.ClickAction = ECardClickAction.None;
			_cardItem.SetData(card);
			if (displayValue is int v)
			{
				_cardItem.SetDisplayValue(v);
			}
		}

		if (_txtCardName != null)
		{
			_txtCardName.Text = Localization.Tr(card.DisplayNameId);
		}

		BindModName(card.Id);
		BindArtist(card.ArtistNameId);

		if (_rtCardDesc != null)
		{
			var store = AppRoot.Services.ContentModPipeline.Registry.Store;
			_rtCardDesc.Text = CardDescBuilder.Build(
				card,
				id => store.TryGetSkill(id, out var skill) ? skill : null,
				Localization.Tr);
		}
	}

	private void BindModName(string cardId)
	{
		if (_txtModName == null)
		{
			return;
		}

		var pipeline = AppRoot.Services.ContentModPipeline;
		if (!pipeline.Registry.TryGetOwnerModId(EContentCategory.Card, cardId, out var modId))
		{
			_txtModName.Text = "";
			return;
		}

		if (pipeline.ScriptCatalog.TryGetDisplayNameKey(modId, out var key)
			&& !string.IsNullOrEmpty(key))
		{
			_txtModName.Text = Localization.Tr(key);
			return;
		}

		_txtModName.Text = modId;
	}

	private void BindArtist(string artistNameId)
	{
		if (_txtArtistName == null)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(artistNameId))
		{
			_txtArtistName.Text = "";
			_txtArtistName.Visible = false;
			return;
		}

		_txtArtistName.Visible = true;
		_txtArtistName.Text = Localization.Tr(artistNameId);
	}

	private void OnMetaHoverStarted(Variant meta)
	{
		var metaStr = meta.AsString();
		if (!CardDescBuilder.TryParseKeywordMeta(metaStr, out var keywordId))
		{
			GD.PushWarning($"CardDetailsDlg: 非法 keyword meta: {metaStr}");
			return;
		}

		if (_rtCardDesc == null)
		{
			return;
		}

		var service = KeywordTipService.Current;
		if (service == null)
		{
			GD.PushWarning("CardDetailsDlg: KeywordTipService.Current 为空。");
			return;
		}

		service.ShowTips(_rtCardDesc, [new KeywordTipRequest(keywordId)]);
	}

	private void OnMetaHoverEnded(Variant _)
	{
		HideKeywordTips();
	}

	private void HideKeywordTips()
	{
		if (_rtCardDesc != null)
		{
			KeywordTipService.Current?.HideTips(_rtCardDesc);
		}
		else
		{
			KeywordTipService.Current?.HideTips();
		}
	}
}
```

注意：
- `EContentCategory`：`using KemoCard.Frame.Content;`
- `KeywordTipService` 已有 `HideTips()` 与 `HideTips(Control anchor)`，优先用带锚点重载。
- **执行顺序：** 先完成 Task 6 Step 1（仅枚举文件），再完成本 Task，最后补 Task 6 打开逻辑，避免 `ECardClickAction` 编译失败。

- [ ] **Step 2: 改 CardDetailsDlg.tscn**

1. 根节点增加 `node_paths` 与 Export 赋值（对齐 CodexDlg 风格）：

```
node_paths=PackedStringArray("_cardItem", "_txtCardName", "_txtModName", "_txtArtistName", "_rtCardDesc")
_cardItem = NodePath("BaseDlgComp/BaseCardItem")
_txtCardName = NodePath("BaseDlgComp/TxtCardName")
_txtModName = NodePath("BaseDlgComp/TxtModName")
_txtArtistName = NodePath("BaseDlgComp/TxtArtistName")
_rtCardDesc = NodePath("BaseDlgComp/RTCardDesc")
```

2. 子节点 `BaseDlgComp/BaseCardItem` 在 Task 6 枚举存在后设置 `ClickAction = 0`（None）。可在编辑器设，或仅靠代码 `InitEvent`/`BindCard` 强制。

3. 占位文案保持翻译键即可（运行时会被覆盖）：`TxtModName` 可改为更合适的占位键或留空；不必新增无用键。

- [ ] **Step 3: 编译检查**

Run: `dotnet build kemo_card.sln`（或项目实际 sln/csproj 名）

Expected: 无错误（需 Task 6 枚举已存在）

- [ ] **Step 4: 格式化 CardDetailsDlg.cs 后 Commit**

```bash
git add Src/mod/global/Ui/CardDetailsDlg.cs Src/mod/global/Ui/CardDetailsDlg.tscn
git commit -m "feat(ui): 实现 CardDetailsDlg 数据绑定与词条悬停"
```

---

### Task 6: BaseCardItem ClickAction + 打开详情

**Files:**
- Create: `Src/mod/global/Def/ECardClickAction.cs`
- Modify: `Src/mod/global/Ui/Comp/BaseCardItem.cs`

- [ ] **Step 1: 创建枚举**

```csharp
namespace KemoCard.Mod.Global.Def;

public enum ECardClickAction
{
	None = 0,
	OpenDetails = 1,
}
```

- [ ] **Step 2: BaseCardItem 增加 Export 与 guinput**

在 `BaseCardItem` 类中：

```csharp
[Export] public ECardClickAction ClickAction { get; set; } = ECardClickAction.OpenDetails;
```

在 `_Ready` 或首次进入树时确保可点（若尚未有 `_Ready`）：

```csharp
public override void _Ready()
{
	MouseFilter = MouseFilterEnum.Stop;
}

public override void _GuiInput(InputEvent @event)
{
	if (ClickAction != ECardClickAction.OpenDetails || _card == null)
	{
		return;
	}

	if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mb
		&& !mb.IsEcho())
	{
		TryOpenDetails();
		AcceptEvent();
	}
}

private void TryOpenDetails()
{
	if (_card == null)
	{
		return;
	}

	var ui = UIManager.Instance;
	if (ui == null)
	{
		GD.PushWarning("BaseCardItem: UIManager.Instance 为空，无法打开卡牌详情。");
		return;
	}

	_ = ui.OpenAsync(
		new UiId<CardDetailsDlgPayload>(GlobalUiIds.CardDetails),
		new CardDetailsDlgPayload
		{
			CardId = _card.Id,
			DisplayValue = _displayOverride,
		});
}
```

说明：Codex 用 `UiId<CodexDlg>` + `default` 是因为无载荷；详情必须传 `CardDetailsDlgPayload`，故 `UiId` 的类型参数为 **Payload 类型**，不是窗口类型。

所需 using：`KemoCard.Frame.UI`、`KemoCard.Frame.UI.Def`、`KemoCard.Mod.Global.Ui`、`KemoCard.Mod.Global.Def`。

- [ ] **Step 3: 编译**

Run: `dotnet build kemo_card.sln`

Expected: PASS

- [ ] **Step 4: 格式化 BaseCardItem.cs / 枚举文件后 Commit**

```bash
git add Src/mod/global/Def/ECardClickAction.cs Src/mod/global/Ui/Comp/BaseCardItem.cs
git commit -m "feat(ui): BaseCardItem 支持 ClickAction 打开卡牌详情"
```

**与 Task 5 的关系：** 推荐 **6 枚举 → 5 对话框 → 6 打开逻辑**；也可本 Task 一次做完（枚举已存在时）。

---

### Task 7: 示例内容与翻译

**Files:**
- Modify: `Config/mods/base-game/content/cards/strike.json`
- Modify: `Config/mods/base-game/content/translations/strings.csv`
- Optional: `strike_plus.json` 同样补 `artistNameId`（可空或同键）

- [ ] **Step 1: strike.json 增加 artistNameId**

```json
"artistNameId": "card.strike.artist",
```

（保持合法 JSON，放在 `displayNameId` 旁。）

- [ ] **Step 2: 追加 mod 翻译**

在 `Config/mods/base-game/content/translations/strings.csv` 追加（`keys,zh_CN,en`）：

```csv
card.strike.name,打击,Strike
card.strike.artist,示例画师,Sample Artist
card.strike_plus.name,打击+,Strike+
skill.strike_hit.name,打击,Strike
skill.strike_hit.desc,造成 6 点伤害。获得 [url=kw:exhaust]消耗[/url]。,Deal 6 damage. Gain [url=kw:exhaust]Exhaust[/url].
```

若这些键已存在，则只改 `skill.strike_hit.desc` 加上 url meta，并补 `card.strike.artist`。

- [ ] **Step 3: Commit**

```bash
git add Config/mods/base-game/content/cards/strike.json Config/mods/base-game/content/translations/strings.csv
git commit -m "content: 为打击卡补充画师与关键词描述示例"
```

---

### Task 8: 全量测试与手动验收

- [ ] **Step 1: 跑相关单测**

Run:

```bash
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "CardDescBuilderTests|TryGetDisplayNameKey|CardDto_round_trips_through_json" -v n
```

Expected: 全部 PASS

- [ ] **Step 2: 手动验收清单（Godot 编辑器运行）**

1. 打开图鉴，左键点击一张卡 → 弹出 `CardDetailsDlg`
2. 卡面、卡名、Mod 名（基础内容）、画师、描述正确
3. 悬停描述中「消耗」→ 出现 exhaust tip；移开消失
4. 再点详情页内卡面 → **不会**再开一层详情
5. 点关闭 → 对话框关闭且 tip 消失
6. 无 `artistNameId` 的卡 → 画师 Label 隐藏

- [ ] **Step 3: 若有修复，单独 commit；无则结束**

---

## Spec 覆盖自检

| Spec 项 | Task |
|---------|------|
| CardDetailsDlg : BaseDlg + 绑定 | 5 |
| UI 注册 | 4 |
| ECardClickAction / 默认 OpenDetails / 详情 None | 6 + 5 |
| artistNameId | 1 + 7 |
| 技能描述拼接 | 3 + 5 |
| kw meta → TipService | 3 + 5 |
| Mod 显示名缓存 | 2 + 5 |
| 示例内容 | 7 |
| 单测 | 1–3, 8 |
| 未知 CardId 关闭 | 5 |
| UIManager 空 Warning | 6 |

---

## 执行交接

Plan 已保存到 `Doc/superpowers/plans/2026-07-13-card-details-dlg-implementation-plan.md`。

两种执行方式：

1. **Subagent-Driven（推荐）** — 每 Task 派一个新子代理，Task 间复查  
2. **Inline Execution** — 本会话按 executing-plans 连续执行并设检查点  

你更想用哪一种？
