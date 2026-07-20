# 角色图鉴与视觉资源 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地角色视觉资源模型（多表情立绘 + 路由 + SpriteFrames）、角色图鉴 Tab、以及带动作切换的轻量角色详情对话框。

**Architecture:** DTO/枚举/Flags 转换器/PortraitResolver 放 `frame`；Query、SummaryBuilder、Presenter、图鉴与详情 UI 放 `mod`。图鉴列表只播 `defaultAnim`；动作切换仅在 `CharacterDetailsDlg`。镜像卡牌图鉴与 `CardDetailsDlg` 模式。

**Tech Stack:** Godot 4.6 Mono / C# / NUnit / System.Text.Json

**Spec:** `Doc/superpowers/specs/2026-07-20-character-codex-design.md`

## Global Constraints

- 面向用户文案必须走本地化键；日志可用明文
- 代码只用 C#；组合优先于继承；布局用 Godot 场景，代码只写逻辑
- `Src/frame/` 不得依赖 `Src/mod/`
- 不创建 `.uid` 文件
- 改完逻辑后对改动过的 `.cs` 执行格式化
- Git 提交说明使用简体中文（仅在用户要求或本计划 Step「Commit」且用户已授权提交时执行）
- 本轮不做：战斗挂接、Spine、Dialogue Manager 接线、列表内动作切换、详情专属卡网格

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `Src/frame/content/definitions/ContentEnums.cs` | 新增 `EPortraitKey`、`EPresentationKind` |
| `Src/frame/content/definitions/CharacterVisualDtos.cs` | `CharacterPortraitsDto` / `PortraitEntryDto` / `CharacterPresentationDto` |
| `Src/frame/content/definitions/CharacterDto.cs` | 增加 `Portraits`、`Presentation`；保留 `ArtPath` |
| `Src/frame/content/definitions/FlagsEnumJsonConverter.cs` | 字符串或字符串数组 → Flags 枚举 |
| `Src/frame/content/definitions/ContentDefinitionJson.cs` | 注册转换器（`ERace`、`EElement`） |
| `Src/frame/content/PortraitResolver.cs` | route/key → path，失败 → Neutral |
| `Src/frame/content/PresentationAnimList.cs` | SpriteFrames 动画名 ∩ 白名单 |
| `Src/mod/global/Def/CodexFilterDefinitions.cs` | 角色过滤字段键、`TryGetRaceLocaleKey` |
| `Src/mod/global/Ui/CharacterCodexQuery.cs` | 角色过滤 / 分页 / CollectTags |
| `Src/mod/global/Ui/CharacterSummaryBuilder.cs` | 悬停 tip |
| `Src/mod/global/Ui/Comp/CharacterPresenter.cs` + `.tscn` | 立绘 / 序列帧绑定与 Play |
| `Src/mod/global/Ui/Comp/BaseCharacterItem.cs` + `.tscn` | 图鉴格 + 悬停 tip + 打开详情 |
| `Src/mod/global/Ui/CharacterDetailsDlg.cs` + `.tscn` + Payload | 详情 + 动作切换 |
| `Src/mod/global/Ui/GlobalUiIds.cs` / `GlobalMod.cs` | 注册 CharacterDetails |
| `Src/mod/global/Ui/CodexDlg.cs` + `.tscn` | 角色 Tab |
| `Resource/Locale/strings.csv` | 新增键 |
| `Config/mods/base-game/content/characters/kemo.json` | 示例 portraits / race |
| `Tests/kemo_card.Ui.Tests/*` | 单测 |

只读参考：`CardCodexQuery.cs`、`CardSummaryBuilder.cs`、`BaseCardItem.cs`、`CardDetailsDlg.cs`、`CodexDlg.cs`。

---

### Task 1: Flags 转换器 + 视觉 DTO + 枚举

**Files:**
- Create: `Src/frame/content/definitions/FlagsEnumJsonConverter.cs`
- Create: `Src/frame/content/definitions/CharacterVisualDtos.cs`
- Modify: `Src/frame/content/definitions/ContentEnums.cs`
- Modify: `Src/frame/content/definitions/CharacterDto.cs`
- Modify: `Src/frame/content/definitions/ContentDefinitionJson.cs`
- Create: `Tests/kemo_card.Ui.Tests/FlagsEnumJsonConverterTests.cs`

**Interfaces:**
- Produces:
  - `enum EPortraitKey { Neutral, Smile, Angry, Sad, Surprised, Hurt, Serious, Happy, Naughty }`
  - `enum EPresentationKind { SpriteFrames, Spine }`
  - `CharacterPortraitsDto` / `PortraitEntryDto` / `CharacterPresentationDto`
  - `CharacterDto.Portraits` / `Presentation`
  - `FlagsEnumJsonConverter<TEnum>` 支持单字符串与字符串数组按位或

- [ ] **Step 1: 写失败测试（Flags 数组）**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using KemoCard.Frame.Content.Definitions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class FlagsEnumJsonConverterTests
{
	private static JsonSerializerOptions Opts()
	{
		var o = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		o.Converters.Add(new JsonStringEnumConverter());
		o.Converters.Add(new FlagsEnumJsonConverter<ERace>());
		return o;
	}

	private sealed class Wrap
	{
		public ERace Race { get; init; }
	}

	[Test]
	public void Deserialize_single_string()
	{
		var w = JsonSerializer.Deserialize<Wrap>("""{"race":"Human"}""", Opts());
		Assert.That(w!.Race, Is.EqualTo(ERace.Human));
	}

	[Test]
	public void Deserialize_string_array_ors_flags()
	{
		var w = JsonSerializer.Deserialize<Wrap>("""{"race":["Human","Canine"]}""", Opts());
		Assert.That(w!.Race, Is.EqualTo(ERace.Human | ERace.Canine));
	}
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~FlagsEnumJsonConverterTests" -v n`

Expected: 编译失败（类型不存在）

- [ ] **Step 3: 实现转换器与 DTO**

`FlagsEnumJsonConverter.cs`：

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class FlagsEnumJsonConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
	public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			return ParseOne(reader.GetString());
		}

		if (reader.TokenType == JsonTokenType.StartArray)
		{
			ulong combined = 0;
			while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
			{
				if (reader.TokenType != JsonTokenType.String)
				{
					throw new JsonException($"Flags enum array expects strings for {typeof(TEnum).Name}.");
				}

				combined |= Convert.ToUInt64(ParseOne(reader.GetString()));
			}

			return (TEnum)Enum.ToObject(typeof(TEnum), combined);
		}

		throw new JsonException($"Unexpected token {reader.TokenType} for {typeof(TEnum).Name}.");
	}

	public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
	{
		writer.WriteStringValue(value.ToString());
	}

	private static TEnum ParseOne(string? name)
	{
		if (string.IsNullOrWhiteSpace(name) || !Enum.TryParse<TEnum>(name, ignoreCase: false, out var v))
		{
			throw new JsonException($"Unknown {typeof(TEnum).Name} value '{name}'.");
		}

		return v;
	}
}
```

在 `ContentEnums.cs` 追加 `EPortraitKey`、`EPresentationKind`（见规格枚举列表）。

`CharacterVisualDtos.cs`：

```csharp
using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

public sealed class CharacterPortraitsDto
{
	[JsonPropertyName("default")]
	public string Default { get; init; } = nameof(EPortraitKey.Neutral);

	[JsonPropertyName("entries")]
	public List<PortraitEntryDto> Entries { get; init; } = [];

	[JsonPropertyName("routes")]
	public Dictionary<string, string> Routes { get; init; } = new(StringComparer.Ordinal);
}

public sealed class PortraitEntryDto
{
	[JsonPropertyName("key")]
	public string Key { get; init; } = "";

	[JsonPropertyName("path")]
	public string Path { get; init; } = "";
}

public sealed class CharacterPresentationDto
{
	[JsonPropertyName("kind")]
	public EPresentationKind Kind { get; init; } = EPresentationKind.SpriteFrames;

	[JsonPropertyName("path")]
	public string Path { get; init; } = "";

	[JsonPropertyName("defaultAnim")]
	public string DefaultAnim { get; init; } = "idle";

	[JsonPropertyName("anims")]
	public List<string>? Anims { get; init; }
}
```

`CharacterDto` 增加：

```csharp
[JsonPropertyName("portraits")]
public CharacterPortraitsDto? Portraits { get; init; }

[JsonPropertyName("presentation")]
public CharacterPresentationDto? Presentation { get; init; }
```

`ContentDefinitionJson.CreateOptions` 在 `JsonStringEnumConverter` 之后：

```csharp
options.Converters.Add(new FlagsEnumJsonConverter<ERace>());
options.Converters.Add(new FlagsEnumJsonConverter<EElement>());
```

注意：注册 Flags 转换器后，单字符串仍走该转换器（不要与 `JsonStringEnumConverter` 对同一类型冲突——`FlagsEnumJsonConverter` 优先处理 `ERace`/`EElement`）。若 STJ 对同类型多转换器行为不确定，可**不要**依赖 `JsonStringEnumConverter` 处理这两类，仅用 Flags 转换器（已支持单字符串）。

- [ ] **Step 4: 格式化并跑测试**

Run: 同上 filter。Expected: PASS

- [ ] **Step 5: Commit（用户要求时）**

```bash
git add Src/frame/content/definitions Tests/kemo_card.Ui.Tests/FlagsEnumJsonConverterTests.cs
git commit -m "$(cat <<'EOF'
支持角色种族/元素 Flags 数组反序列化，并扩展视觉 DTO

EOF
)"
```

---

### Task 2: PortraitResolver + PresentationAnimList

**Files:**
- Create: `Src/frame/content/PortraitResolver.cs`
- Create: `Src/frame/content/PresentationAnimList.cs`
- Create: `Tests/kemo_card.Ui.Tests/PortraitResolverTests.cs`
- Create: `Tests/kemo_card.Ui.Tests/PresentationAnimListTests.cs`

**Interfaces:**
- Produces:
  - `PortraitResolver.ResolveByRoute(CharacterDto, string routeKey, Func<string, bool> pathExists) -> string`
  - `PortraitResolver.ResolveByKey(CharacterDto, string portraitKey, Func<string, bool> pathExists) -> string`
  - `PortraitResolver.NormalizePortraits(CharacterDto) -> CharacterPortraitsDto`（artPath 兼容）
  - `PresentationAnimList.Resolve(IReadOnlyList<string> framesAnims, IReadOnlyList<string>? whitelist) -> IReadOnlyList<string>`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class PortraitResolverTests
{
	private static CharacterDto Char(
		string? artPath = null,
		CharacterPortraitsDto? portraits = null) => new()
	{
		Id = "kemo",
		ArtPath = artPath ?? "",
		Portraits = portraits,
	};

	[Test]
	public void ArtPath_only_acts_as_neutral()
	{
		var c = Char(artPath: "chars/kemo.png");
		Assert.That(
			PortraitResolver.ResolveByKey(c, nameof(EPortraitKey.Neutral), _ => true),
			Is.EqualTo("chars/kemo.png"));
	}

	[Test]
	public void Missing_route_falls_back_to_neutral()
	{
		var c = Char(portraits: new CharacterPortraitsDto
		{
			Entries =
			[
				new() { Key = "Neutral", Path = "n.png" },
				new() { Key = "Happy", Path = "h.png" },
			],
			Routes = new Dictionary<string, string> { ["intro"] = "Happy" },
		});
		Assert.That(PortraitResolver.ResolveByRoute(c, "missing", p => p is "n.png" or "h.png"), Is.EqualTo("n.png"));
	}

	[Test]
	public void Route_target_missing_file_falls_back_to_neutral()
	{
		var c = Char(portraits: new CharacterPortraitsDto
		{
			Entries =
			[
				new() { Key = "Neutral", Path = "n.png" },
				new() { Key = "Happy", Path = "h.png" },
			],
			Routes = new Dictionary<string, string> { ["intro"] = "Happy" },
		});
		Assert.That(PortraitResolver.ResolveByRoute(c, "intro", p => p == "n.png"), Is.EqualTo("n.png"));
	}

	[Test]
	public void Custom_key_route_works()
	{
		var c = Char(portraits: new CharacterPortraitsDto
		{
			Entries =
			[
				new() { Key = "Neutral", Path = "n.png" },
				new() { Key = "custom:wave", Path = "w.png" },
			],
			Routes = new Dictionary<string, string> { ["hi"] = "custom:wave" },
		});
		Assert.That(PortraitResolver.ResolveByRoute(c, "hi", _ => true), Is.EqualTo("w.png"));
	}
}

[TestFixture]
public sealed class PresentationAnimListTests
{
	[Test]
	public void Null_whitelist_returns_all_sorted()
	{
		Assert.That(
			PresentationAnimList.Resolve(["hurt", "idle"], null),
			Is.EqualTo(new[] { "hurt", "idle" }));
	}

	[Test]
	public void Whitelist_intersects()
	{
		Assert.That(
			PresentationAnimList.Resolve(["idle", "hurt", "cast"], ["idle", "cast", "missing"]),
			Is.EqualTo(new[] { "cast", "idle" }));
	}
}
```

（`PresentationAnimList` 返回按 Ordinal 排序的稳定列表。）

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~PortraitResolverTests|FullyQualifiedName~PresentationAnimListTests" -v n`

- [ ] **Step 3: 实现**

`PortraitResolver` 要点：

- `NormalizePortraits`：若 `Portraits==null` 且 `ArtPath` 非空 → entries 仅 Neutral；若已有 Portraits，合并时不覆盖已有 Neutral
- `ResolveByRoute`：查 routes → `ResolveByKey`；无 route → Neutral
- `ResolveByKey`：查 entries（Ordinal）；`pathExists(path)` 为 false → Neutral 路径；Neutral 也不存在 → `""`
- key 比较：Ordinal；枚举名大小写与 JSON 一致（PascalCase）

`PresentationAnimList.Resolve`：whitelist 空或 null → 全部；否则交集；过滤空白；`OrderBy(Ordinal)`。

- [ ] **Step 4: 跑测试 PASS 并格式化**

- [ ] **Step 5: Commit（用户要求时）**

```bash
git commit -m "$(cat <<'EOF'
实现 PortraitResolver 与 PresentationAnimList 纯逻辑

EOF
)"
```

---

### Task 3: CharacterCodexQuery + 种族本地化

**Files:**
- Create: `Src/mod/global/Ui/CharacterCodexQuery.cs`
- Modify: `Src/mod/global/Def/CodexFilterDefinitions.cs`
- Modify: `Resource/Locale/strings.csv`
- Create: `Tests/kemo_card.Ui.Tests/CharacterCodexQueryTests.cs`

**Interfaces:**
- Produces:
  - `enum ECharFilterField { Element, Role, Race, Tag }`
  - `readonly record struct CharFilterCondition(ECharFilterField Field, ECardFilterOp Op, string ValueId, string DisplayText)`（复用 `ECardFilterOp`）
  - `CharacterCodexQuery.OpsForField` / `MatchesCondition` / `Filter` / `SlicePage` / `TotalPages` / `CollectTags` / `PageSize=8`
  - `CodexFilterDefinitions.GetCharFieldLocaleKey` / `TryGetRaceLocaleKey`

- [ ] **Step 1: 写失败测试（种族 Flags）**

```csharp
[Test]
public void Race_contains_and_exact()
{
	var c = new CharacterDto { Id = "a", Race = ERace.Human | ERace.Canine };
	Assert.That(CharacterCodexQuery.MatchesCondition(c, new(ECharFilterField.Race, ECardFilterOp.Contains, nameof(ERace.Human), "")), Is.True);
	Assert.That(CharacterCodexQuery.MatchesCondition(c, new(ECharFilterField.Race, ECardFilterOp.Exact, nameof(ERace.Human), "")), Is.False);
	var single = new CharacterDto { Id = "b", Race = ERace.Human };
	Assert.That(CharacterCodexQuery.MatchesCondition(single, new(ECharFilterField.Race, ECardFilterOp.Exact, nameof(ERace.Human), "")), Is.True);
}
```

另补 Element Contains/Exact、Role Equal、Tag、Filter 文本与分页测试（镜像 `CardCodexQueryTests`）。

- [ ] **Step 2: 跑测试失败 → 实现 Query**

元素/种族匹配：与 `CardCodexQuery.MatchElement` 相同位运算（`CharacterDto.Element`/`Race` 转 `(int)`）。

`Filter`：全部角色（本轮无 HideInDex）；条件 AND；文本匹配 `DisplayNameId`/`DescId` 译文与技能 DescId；按 Id 排序。

- [ ] **Step 3: CodexFilterDefinitions + CSV**

追加：

```csv
UI_CHARACTER,角色,Character
UI_CODEX_FILTER_RACE,种族,Race
UI_CODEX_CHAR_TXT_FILTER,角色名或描述,Character Name Or Desc
UI_CODEX_ANIM,动作,Anim
UI_CHARACTER_DETAILS_TITLE,角色详情,Character Details
UI_RACE_HUMAN,人族,Human
UI_RACE_CANINE,犬科,Canine
UI_RACE_FELINE,猫科,Feline
UI_RACE_BIRD,鸟类,Bird
UI_RACE_INSECT,昆虫,Insect
UI_RACE_BEAST,野兽,Beast
UI_RACE_FISH,水族,Fish
UI_RACE_REPTILE,爬行,Reptile
UI_RACE_PLANT,植物,Plant
UI_RACE_MACHINE,机械,Machine
UI_RACE_DEMONIC,恶魔,Demonic
UI_RACE_ANGEL,天使,Angel
UI_RACE_DRAGON,龙族,Dragon
UI_RACE_GOD,神族,God
UI_RACE_DEVIL,魔族,Devil
UI_RACE_UNDEAD,不死,Undead
UI_RACE_UNKNOWN,未知,Unknown
```

`TryGetRaceLocaleKey` 映射各 `ERace` 单 bit（跳过 None；`UnKnown` → `UI_RACE_UNKNOWN`）。

- [ ] **Step 4: 测试 PASS + Commit（用户要求时）**

---

### Task 4: CharacterSummaryBuilder

**Files:**
- Create: `Src/mod/global/Ui/CharacterSummaryBuilder.cs`
- Create: `Tests/kemo_card.Ui.Tests/CharacterSummaryBuilderTests.cs`

**Interfaces:**
- Produces: `CharacterSummaryTip` + `Build(CharacterDto, resolveSkill, translate)`
- 复用 `CardSummaryBuilder.StripRichText`（若为 internal/同程序集可见；否则提取到共享 helper 或复制剥离逻辑到本 Builder 私有——优先调用已有 `public` `StripRichText`）

- [ ] **Step 1: 失败测试**

Title = 译名；Body 第 1 行 = 多元素 `、` + 职业 + 多种族 `、`，段间空格；其后技能纯文本。

- [ ] **Step 2: 实现并 PASS**

元素用 `CodexFilterDefinitions.TryGetElementLocaleKey`；种族遍历 `ERace` flags（跳过 None）；职业跳过 `ERole.None`。

- [ ] **Step 3: Commit（用户要求时）**

---

### Task 5: CharacterPresenter 场景组件

**Files:**
- Create: `Src/mod/global/Ui/Comp/CharacterPresenter.cs`
- Create: `Src/mod/global/Ui/Comp/CharacterPresenter.tscn`（Godot 编辑器：根 Control + `TextureRect` 立绘 + `AnimatedSprite2D`）

**Interfaces:**
- Produces:
  - `void Bind(CharacterDto character)`
  - `void Play(string animName)`
  - `IReadOnlyList<string> ListAnims()`
  - `bool HasPresentation { get; }`

- [ ] **Step 1: 实现 Bind/Play/ListAnims**

逻辑：

1. 若 `Presentation?.Kind == SpriteFrames` 且 path 可加载为 `SpriteFrames` → 显示 AnimatedSprite2D，隐藏 TextureRect；`Play(DefaultAnim)`；`ListAnims` = `PresentationAnimList.Resolve(frames.GetAnimationNames(), Presentation.Anims)`
2. 否则立绘：`PortraitResolver.ResolveByKey(..., Neutral, pathExists)`，加载纹理（复用 `BaseCardItem` 同款 Mod/`res://Resource/Assets/` 解析，可抽 `CharacterArtLoader` 静态工具到 `mod/global/Ui` 避免复制；若抽取过重则先复制最小加载函数）
3. Spine kind：Warning 并走立绘回退

`pathExists`：对相对 path 检查 Mod 文件或 `ResourceLoader.Exists(resPath)`。

- [ ] **Step 2: 在编辑器建 `.tscn`，Export 绑定节点**

- [ ] **Step 3: 编译** `dotnet build` Expected: 成功

- [ ] **Step 4: Commit（用户要求时）**

---

### Task 6: BaseCharacterItem + CharacterDetailsDlg

**Files:**
- Create: `Src/mod/global/Def/ECharacterClickAction.cs`（`None` / `OpenDetails`）
- Create: `Src/mod/global/Ui/Comp/BaseCharacterItem.cs` + `.tscn`
- Create: `Src/mod/global/Ui/CharacterDetailsDlg.cs` + `.tscn`
- Modify: `Src/mod/global/Ui/GlobalUiIds.cs`
- Modify: `Src/mod/global/GlobalMod.cs`

**Interfaces:**
- Produces:
  - `CharacterDetailsDlgPayload { string CharacterId }`
  - `GlobalUiIds.CharacterDetails = "CharacterDetailsDlg"`
  - `BaseCharacterItem.SetData` / `EnableHoverTip` / `ClickAction`

- [ ] **Step 1: 注册 UI**

```csharp
public const string CharacterDetails = "CharacterDetailsDlg";
// GlobalMod:
yield return UIRegistration.Dialog(GlobalUiIds.CharacterDetails, "Src/mod/global/Ui");
```

- [ ] **Step 2: CharacterDetailsDlg**

镜像 `CardDetailsDlg`：

- Export：`CharacterPresenter`、名称 Label、描述 Label/RichTextLabel、动作 `OptionButton`
- `OnOpen`：按 `CharacterId` `TryGetCharacter`；失败 Warning+Close
- Bind presenter；填充 `ListAnims` 到 OptionButton；`ItemSelected` → `Play`
- 无 presentation：`OptionButton.Visible = false`
- 详情内嵌 Presenter/`BaseCharacterItem` 若存在点击，设 `ClickAction = None`

- [ ] **Step 3: BaseCharacterItem**

- 组合 `CharacterPresenter`（或内嵌同逻辑）；`SetData` → Bind
- 悬停 tip：对齐 `BaseCardItem`，调用 `CharacterSummaryBuilder` + `ShowCustomTips`
- 点击 `OpenDetails` → `UIManager.OpenAsync(new UiId<CharacterDetailsDlgPayload>(...), new() { CharacterId = id })`

- [ ] **Step 4: 编辑器布局场景（详情标题用 `UI_CHARACTER_DETAILS_TITLE`）**

- [ ] **Step 5: 编译 + Commit（用户要求时）**

---

### Task 7: CodexDlg 角色 Tab

**Files:**
- Modify: `Src/mod/global/Ui/CodexDlg.cs`
- Modify: `Src/mod/global/Ui/CodexDlg.tscn`（新增 `UI_CHARACTER` Tab：过滤控件可复用一套 OptionButton 或复制一组；角色 `GridContainer` + 8×`BaseCharacterItem`；与卡牌共用或分用 `BasePager`——**推荐 Tab 切换时绑定同一套过滤控件到当前 Tab 的字段枚举，两套条件列表分字段保存**）

**Interfaces:**
- Consumes: `CharacterCodexQuery`、卡牌侧现有逻辑不变

- [ ] **Step 1: 扩展 CodexDlg 状态**

```csharp
private enum CodexTab { Card, Character }
private CodexTab _tab;
private readonly List<CharFilterCondition> _charConditions = [];
private IReadOnlyList<CharacterDto> _filteredCharacters = [];
private List<BaseCharacterItem> _charSlots = [];
```

- [ ] **Step 2: Tab 切换**

- 切到 Character：Populate 字段为 Element/Role/Race/Tag；Ops/Val 按 `CharacterCodexQuery.OpsForField`；文本 placeholder 用 `UI_CODEX_CHAR_TXT_FILTER`
- 切到 Card：恢复卡牌字段逻辑
- 切换时 `KeywordTipService.Current?.HideTips()`；刷新当前 Tab 列表

- [ ] **Step 3: 角色 Refresh/Fill**

镜像卡牌 `RefreshFilteredList` / `FillCurrentPage`，数据源 `store.Characters.Values`。

- [ ] **Step 4: OnOpen 清空两边条件，默认 Card Tab**

- [ ] **Step 5: 场景中增加角色 Tab 与网格；卡槽 `EnableHoverTip=true`**

- [ ] **Step 6: 编译 + 手动冒烟**

1. 打开图鉴 → 角色 Tab → 见角色格
2. 过滤种族 Contains、搜索、分页
3. 悬停 tip
4. 点击打开详情 → 切换动作（有 presentation 时）；无 presentation 时动作隐藏
5. 卡牌 Tab 行为回归

- [ ] **Step 7: 更新 `kemo.json` 示例**（race 数组 + 可选 portraits；presentation 可暂空）

- [ ] **Step 8: Commit（用户要求时）**

```bash
git commit -m "$(cat <<'EOF'
图鉴增加角色 Tab，并支持角色详情与动作切换

EOF
)"
```

---

## Spec 覆盖自检

| Spec 条目 | Task |
|-----------|------|
| portraits + routes + artPath 兼容 | Task 1–2 |
| PortraitResolver fallback Neutral | Task 2 |
| presentation SpriteFrames + anims 白名单 | Task 2、5 |
| CharacterPresenter | Task 5 |
| 过滤 Element/Role/Race/Tag Flags | Task 3 |
| Summary tip | Task 4 |
| Codex 角色 Tab、列表仅 defaultAnim | Task 7 |
| CharacterDetailsDlg 动作切换 | Task 6 |
| 本地化 | Task 3、6 |
| 不做战斗/Spine/DM/列表动作切换 | 全局约束 |

---

## 执行交接

Plan 已保存至 `Doc/superpowers/plans/2026-07-20-character-codex-implementation-plan.md`。

**两种执行方式：**

1. **Subagent-Driven（推荐）** — 每 Task 新开子代理，Task 间复查  
2. **Inline Execution** — 本会话按 executing-plans 连续执行并设检查点  

你更倾向哪一种？
