# BaseCardItem 卡牌基础组件 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 `BaseCardItem` 的数据绑定与分项刷新 API，并为 `CardDto.baseValue`、UI Definitions、Mod content 根解析提供配套支持。

**Architecture:** 纯展示组件：`SetData(CardDto?)` 整卡刷新，`SetDisplayValue(int?)` 覆盖数值；分项 Set 只改对应节点。立绘经 `AppRoot` → `ContentModPipeline.Registry` + `ModScriptCatalog.TryGetContentRootPath` 做 Mod 文件 → `res://Resource/Assets/` 回退。类型键 / 卡框路径 / 元素色拆分放在 `Src/mod/global/Def/`，供组件与单测共用。

**Tech Stack:** Godot 4.6 Mono / C# / NUnit / System.Text.Json

**Spec:** `Doc/superpowers/specs/2026-07-09-base-card-item-design.md`

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `Src/frame/content/definitions/CardDto.cs` | 增加 `BaseValue` |
| `Src/mod/global/Def/Definitions.cs` | 颜色常量 + 卡类型键 / 卡框路径 / 元素色收集 |
| `Resource/Locale/strings.csv` | `UI_CARD_TYPE_*` 短标签 |
| `Src/frame/scripting/ModScriptCatalog.cs` | 缓存 ContentRoot，提供 `TryGetContentRootPath` |
| `Src/frame/content/ContentModPipeline.cs` | 只读暴露 `ScriptCatalog` |
| `Src/mod/global/Ui/Comp/BaseCardItem.cs` | 绑定与刷新逻辑 |
| `Src/mod/global/Ui/Comp/BaseCardItem.tscn` | `node_paths`、清理占位文案 |
| `Config/mods/base-game/content/cards/strike.json` | 补 `baseValue` |
| `Config/mods/base-game/content/cards/strike_plus.json` | 补 `baseValue` |
| `Tests/kemo_card.Ui.Tests/ContentDefinitionTests.cs` | `baseValue` JSON 往返断言 |
| `Tests/kemo_card.Ui.Tests/CardUiDefinitionsTests.cs` | Definitions / 元素色单测 |
| `Tests/kemo_card.Ui.Tests/ModScriptCatalogTests.cs` | content 根路径单测 |

---

### Task 1: CardDto 增加 baseValue

**Files:**
- Modify: `Src/frame/content/definitions/CardDto.cs`
- Modify: `Tests/kemo_card.Ui.Tests/ContentDefinitionTests.cs`
- Modify: `Config/mods/base-game/content/cards/strike.json`
- Modify: `Config/mods/base-game/content/cards/strike_plus.json`

- [ ] **Step 1: 写失败测试 — JSON 往返含 baseValue**

在 `ContentDefinitionTests.CardDto_round_trips_through_json` 中为 `original` 增加 `BaseValue = 6`，并断言：

```csharp
Assert.That(restored!.BaseValue, Is.EqualTo(6));
```

同时在序列化结果中断言含字段名（可选加固）：

```csharp
Assert.That(json, Does.Contain("baseValue"));
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~CardDto_round_trips_through_json"
```

Expected: FAIL（`CardDto` 尚无 `BaseValue`，或断言失败）

- [ ] **Step 3: 实现 CardDto 字段**

在 `CardDto.cs` 的 `Cost` 属性后增加：

```csharp
[JsonPropertyName("baseValue")]
public int BaseValue { get; init; }
```

- [ ] **Step 4: 示例卡 JSON 补 baseValue**

`strike.json` 增加 `"baseValue": 6`（与现有 strike 伤害占位一致即可）。

`strike_plus.json` 增加 `"baseValue": 9`。

- [ ] **Step 5: 运行测试确认通过**

Run: 同 Step 2  
Expected: PASS

- [ ] **Step 6: Commit**

```powershell
git add Src/frame/content/definitions/CardDto.cs Tests/kemo_card.Ui.Tests/ContentDefinitionTests.cs Config/mods/base-game/content/cards/strike.json Config/mods/base-game/content/cards/strike_plus.json
git commit -m "feat(content): CardDto 增加 baseValue 基础展示数值"
```

---

### Task 2: CardUiDefinitions（类型键 / 卡框路径 / 元素色）

**Files:**
- Modify: `Src/mod/global/Def/Definitions.cs`
- Create: `Tests/kemo_card.Ui.Tests/CardUiDefinitionsTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `Tests/kemo_card.Ui.Tests/CardUiDefinitionsTests.cs`：

```csharp
using Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Def;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class CardUiDefinitionsTests
{
	[Test]
	public void CardTypeLocaleKeys_cover_all_card_types()
	{
		foreach (ECardType type in Enum.GetValues<ECardType>())
		{
			Assert.That(CardUiDefinitions.TryGetCardTypeLocaleKey(type, out var key), Is.True, type.ToString());
			Assert.That(key, Does.StartWith("UI_CARD_TYPE_"));
		}
	}

	[Test]
	public void CardFramePaths_cover_all_rarities()
	{
		foreach (ERarity rarity in Enum.GetValues<ERarity>())
		{
			Assert.That(CardUiDefinitions.TryGetCardFramePath(rarity, out var path), Is.True, rarity.ToString());
			Assert.That(path, Does.StartWith("res://Resource/Assets/CardFrame/"));
			Assert.That(path, Does.EndWith(".png"));
		}
	}

	[Test]
	public void CollectElementColors_none_flags_returns_none_color()
	{
		var colors = CardUiDefinitions.CollectElementColors(0);
		Assert.That(colors, Has.Length.EqualTo(1));
		Assert.That(colors[0], Is.EqualTo(ColorDefinitions.NoneElement));
	}

	[Test]
	public void CollectElementColors_single_red()
	{
		var colors = CardUiDefinitions.CollectElementColors((int)EElement.Red);
		Assert.That(colors, Has.Length.EqualTo(1));
		Assert.That(colors[0], Is.EqualTo(ColorDefinitions.RedElement));
	}

	[Test]
	public void CollectElementColors_three_elements_in_flag_order()
	{
		var flags = (int)(EElement.Red | EElement.Blue | EElement.Green);
		var colors = CardUiDefinitions.CollectElementColors(flags);
		Assert.That(colors, Has.Length.EqualTo(3));
		Assert.That(colors[0], Is.EqualTo(ColorDefinitions.RedElement));
		Assert.That(colors[1], Is.EqualTo(ColorDefinitions.BlueElement));
		Assert.That(colors[2], Is.EqualTo(ColorDefinitions.GreenElement));
	}

	[Test]
	public void CollectElementColors_more_than_three_truncates()
	{
		var flags = (int)(EElement.Red | EElement.Blue | EElement.Green | EElement.Yellow);
		var colors = CardUiDefinitions.CollectElementColors(flags);
		Assert.That(colors, Has.Length.EqualTo(3));
		Assert.That(colors[0], Is.EqualTo(ColorDefinitions.RedElement));
		Assert.That(colors[1], Is.EqualTo(ColorDefinitions.BlueElement));
		Assert.That(colors[2], Is.EqualTo(ColorDefinitions.GreenElement));
	}

	[Test]
	public void TryGetElementColor_maps_known_elements()
	{
		Assert.That(CardUiDefinitions.TryGetElementColor(EElement.Yellow, out var c), Is.True);
		Assert.That(c, Is.EqualTo(ColorDefinitions.YellowElement));
		Assert.That(CardUiDefinitions.TryGetElementColor(EElement.None, out _), Is.False);
	}

	[Test]
	public void FormatCost_rules()
	{
		Assert.That(CardUiDefinitions.FormatCost(ECostType.None, 3), Is.EqualTo(""));
		Assert.That(CardUiDefinitions.FormatCost(ECostType.X, 0), Is.EqualTo("X"));
		Assert.That(CardUiDefinitions.FormatCost(ECostType.Energy, 2), Is.EqualTo("2"));
	}
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~CardUiDefinitionsTests"
```

Expected: FAIL（类型不存在）

- [ ] **Step 3: 实现 Definitions**

将 `Src/mod/global/Def/Definitions.cs` 替换/扩展为（保留现有颜色常量，补命名空间与 `CardUiDefinitions`）：

```csharp
using Godot;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Global.Def;

public static class ColorDefinitions
{
	public static readonly Color NoneElement = new("#747474");
	public static readonly Color RedElement = new("#f10101");
	public static readonly Color BlueElement = new("#3099f2");
	public static readonly Color GreenElement = new("#28ff00");
	public static readonly Color YellowElement = new("#ffc800");
	/// <summary>阴</summary>
	public static readonly Color YinElement = new("#000000");
	/// <summary>阳</summary>
	public static readonly Color YangElement = new("#ffffff");
}

public static class CardUiDefinitions
{
	private static readonly Dictionary<ECardType, string> CardTypeLocaleKeys = new()
	{
		[ECardType.Physics] = "UI_CARD_TYPE_PHYSICS",
		[ECardType.Magical] = "UI_CARD_TYPE_MAGICAL",
		[ECardType.Support] = "UI_CARD_TYPE_SUPPORT",
		[ECardType.Guard] = "UI_CARD_TYPE_GUARD",
		[ECardType.Resist] = "UI_CARD_TYPE_RESIST",
		[ECardType.Weak] = "UI_CARD_TYPE_WEAK",
		[ECardType.Counter] = "UI_CARD_TYPE_COUNTER",
		[ECardType.Healing] = "UI_CARD_TYPE_HEALING",
		[ECardType.Curse] = "UI_CARD_TYPE_CURSE",
	};

	private static readonly Dictionary<ERarity, string> CardFramePaths = new()
	{
		[ERarity.Common] = "res://Resource/Assets/CardFrame/Common.png",
		[ERarity.Uncommon] = "res://Resource/Assets/CardFrame/Uncommon.png",
		[ERarity.Rare] = "res://Resource/Assets/CardFrame/Rare.png",
		[ERarity.Epic] = "res://Resource/Assets/CardFrame/Epic.png",
		[ERarity.Legendary] = "res://Resource/Assets/CardFrame/Legendary.png",
	};

	private static readonly EElement[] ElementOrder =
	[
		EElement.Red,
		EElement.Blue,
		EElement.Green,
		EElement.Yellow,
		EElement.Yin,
		EElement.Yang,
	];

	public static bool TryGetCardTypeLocaleKey(ECardType type, out string key) =>
		CardTypeLocaleKeys.TryGetValue(type, out key!);

	public static bool TryGetCardFramePath(ERarity rarity, out string path) =>
		CardFramePaths.TryGetValue(rarity, out path!);

	public static bool TryGetElementColor(EElement element, out Color color)
	{
		switch (element)
		{
			case EElement.Red:
				color = ColorDefinitions.RedElement;
				return true;
			case EElement.Blue:
				color = ColorDefinitions.BlueElement;
				return true;
			case EElement.Green:
				color = ColorDefinitions.GreenElement;
				return true;
			case EElement.Yellow:
				color = ColorDefinitions.YellowElement;
				return true;
			case EElement.Yin:
				color = ColorDefinitions.YinElement;
				return true;
			case EElement.Yang:
				color = ColorDefinitions.YangElement;
				return true;
			default:
				color = default;
				return false;
		}
	}

	/// <summary>
	/// 按 EElement 位顺序收集最多 3 色；无有效位时返回单色 NoneElement。
	/// </summary>
	public static Color[] CollectElementColors(int flags)
	{
		var list = new List<Color>(3);
		foreach (var element in ElementOrder)
		{
			if ((flags & (int)element) == 0)
			{
				continue;
			}

			if (!TryGetElementColor(element, out var color))
			{
				continue;
			}

			list.Add(color);
			if (list.Count >= 3)
			{
				break;
			}
		}

		if (list.Count == 0)
		{
			return [ColorDefinitions.NoneElement];
		}

		return list.ToArray();
	}

	public static string FormatCost(ECostType costType, int cost) =>
		costType switch
		{
			ECostType.None => "",
			ECostType.X => "X",
			_ => cost.ToString(),
		};
}
```

注意：若工程中已有无命名空间的 `ColorDefinitions` 引用，同步改为 `KemoCard.Mod.Global.Def.ColorDefinitions`（当前仅新文件，预期无其它引用）。

- [ ] **Step 4: 运行测试确认通过**

Run: 同 Step 2  
Expected: PASS

- [ ] **Step 5: Commit**

```powershell
git add Src/mod/global/Def/Definitions.cs Tests/kemo_card.Ui.Tests/CardUiDefinitionsTests.cs
git commit -m "feat(ui): 增加卡牌 UI Definitions（类型键/卡框/元素色）"
```

---

### Task 3: 本地化键 UI_CARD_TYPE_*

**Files:**
- Modify: `Resource/Locale/strings.csv`

- [ ] **Step 1: 追加翻译行**

在 `strings.csv` 末尾追加（保持 CSV 三列）：

```csv
UI_CARD_TYPE_PHYSICS,物,Phy
UI_CARD_TYPE_MAGICAL,魔,Mag
UI_CARD_TYPE_SUPPORT,支,Sup
UI_CARD_TYPE_GUARD,防,Grd
UI_CARD_TYPE_RESIST,抗,Rst
UI_CARD_TYPE_WEAK,弱,Wek
UI_CARD_TYPE_COUNTER,反,Ctr
UI_CARD_TYPE_HEALING,疗,Heal
UI_CARD_TYPE_CURSE,咒,Cur
```

- [ ] **Step 2: Commit**

```powershell
git add Resource/Locale/strings.csv
git commit -m "feat(locale): 增加卡牌类型短标签翻译键"
```

说明：`.translation` 二进制由 Godot 导入生成；若仓库跟踪了 `strings.*.translation`，在编辑器打开项目后再一并提交生成结果。本任务以 CSV 为准。

---

### Task 4: ModScriptCatalog 支持 content 根

**Files:**
- Modify: `Src/frame/scripting/ModScriptCatalog.cs`
- Create: `Tests/kemo_card.Ui.Tests/ModScriptCatalogTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ModScriptCatalogTests
{
	[Test]
	public void TryGetContentRootPath_combines_folder_and_content_root()
	{
		var root = Path.Combine(Path.GetTempPath(), "kemo_catalog_tests", Guid.NewGuid().ToString("N"));
		var modDir = Path.Combine(root, "base-game");
		Directory.CreateDirectory(modDir);

		var catalog = new ModScriptCatalog();
		catalog.Rebuild(
		[
			new DiscoveredModEntry(
				modDir,
				new ContentModManifestDto { ModId = "base.game", ContentRoot = "content" }),
		]);

		Assert.That(catalog.TryGetFolderPath("base.game", out var folder), Is.True);
		Assert.That(folder, Is.EqualTo(modDir));

		Assert.That(catalog.TryGetContentRootPath("base.game", out var contentRoot), Is.True);
		Assert.That(Path.GetFullPath(contentRoot), Is.EqualTo(Path.GetFullPath(Path.Combine(modDir, "content"))));
	}

	[Test]
	public void TryGetContentRootPath_unknown_mod_returns_false()
	{
		var catalog = new ModScriptCatalog();
		Assert.That(catalog.TryGetContentRootPath("missing", out _), Is.False);
	}
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~ModScriptCatalogTests"
```

Expected: FAIL（`TryGetContentRootPath` 不存在）

- [ ] **Step 3: 实现 catalog**

将 `ModScriptCatalog.cs` 改为：

```csharp
using KemoCard.Frame.Content;

namespace KemoCard.Frame.Scripting;

public sealed class ModScriptCatalog
{
	private readonly Dictionary<string, (string FolderPath, string ContentRoot)> _byModId =
		new(StringComparer.Ordinal);

	public void Rebuild(IReadOnlyList<DiscoveredModEntry> activeMods)
	{
		_byModId.Clear();
		foreach (var entry in activeMods)
		{
			var contentRoot = string.IsNullOrWhiteSpace(entry.Manifest.ContentRoot)
				? "content"
				: entry.Manifest.ContentRoot;
			_byModId[entry.Manifest.ModId] = (entry.FolderPath, contentRoot);
		}
	}

	public bool TryGetFolderPath(string modId, out string folderPath)
	{
		if (_byModId.TryGetValue(modId, out var entry))
		{
			folderPath = entry.FolderPath;
			return true;
		}

		folderPath = null!;
		return false;
	}

	public bool TryGetContentRootPath(string modId, out string contentRootPath)
	{
		if (_byModId.TryGetValue(modId, out var entry))
		{
			contentRootPath = Path.Combine(entry.FolderPath, entry.ContentRoot);
			return true;
		}

		contentRootPath = null!;
		return false;
	}
}
```

- [ ] **Step 4: 运行相关测试确认通过**

Run:

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~ModScriptCatalogTests|FullyQualifiedName~ModScriptLoaderTests"
```

Expected: PASS（`TryGetFolderPath` 行为保持，脚本加载测试不回归）

- [ ] **Step 5: Commit**

```powershell
git add Src/frame/scripting/ModScriptCatalog.cs Tests/kemo_card.Ui.Tests/ModScriptCatalogTests.cs
git commit -m "feat(content): ModScriptCatalog 支持解析 Mod content 根路径"
```

---

### Task 5: ContentModPipeline 暴露 ScriptCatalog

**Files:**
- Modify: `Src/frame/content/ContentModPipeline.cs`

- [ ] **Step 1: 增加只读属性**

在 `public GameDefinitionRegistry Registry { get; }` 旁增加：

```csharp
public ModScriptCatalog ScriptCatalog => _scriptCatalog;
```

确认文件顶部已有 `using KemoCard.Frame.Scripting;`（已有则不动）。

- [ ] **Step 2: 编译验证**

Run:

```powershell
dotnet build Src/frame/content/ContentModPipeline.cs --no-restore 2>$null; dotnet build kemo_card.csproj
```

（若单文件 build 不适用，直接：）

```powershell
dotnet build kemo_card.csproj
```

Expected: 成功

- [ ] **Step 3: Commit**

```powershell
git add Src/frame/content/ContentModPipeline.cs
git commit -m "feat(content): ContentModPipeline 暴露 ScriptCatalog"
```

---

### Task 6: 实现 BaseCardItem 逻辑

**Files:**
- Modify: `Src/mod/global/Ui/Comp/BaseCardItem.cs`

- [ ] **Step 1: 实现完整组件**

用下列实现替换 `BaseCardItem.cs`（布局仍在场景；代码只做绑定。超过 5 个业务方法时用 `#region`）：

```csharp
using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Def;

namespace KemoCard.Mod.Global.Ui.Comp;

public partial class BaseCardItem : Control
{
	[Export] private TextureRect? _trArt;
	[Export] private TextureRect? _trCardFrame;
	[Export] private Label? _txtCardCost;
	[Export] private Label? _txtCardType;
	[Export] private Label? _txtCardVal;
	[Export] private ColorRect? _crAttr;

	private CardDto? _card;
	private int _baseValue;
	private int? _displayOverride;
	private ECostType _costType = ECostType.None;

	#region 整体绑定

	public void SetData(CardDto? card)
	{
		_card = card;
		_baseValue = card?.BaseValue ?? 0;
		_displayOverride = null;
		_costType = card?.CostType ?? ECostType.None;

		if (card is null)
		{
			ClearVisuals();
			return;
		}

		ApplyCost(card.CostType, card.Cost);
		SetCardType(card.CardType);
		RefreshValueLabel();
		SetElement(card.Element);
		SetArtFromPath(card.ArtPath, card.Id);
		SetCardFrameFromRarity(card.Rarity);
	}

	public void SetDisplayValue(int? value)
	{
		_displayOverride = value;
		RefreshValueLabel();
	}

	#endregion

	#region 分项接口

	public void SetCost(int cost)
	{
		ApplyCost(_costType, cost);
	}

	public void SetCostVisible(bool visible)
	{
		if (_txtCardCost != null)
		{
			_txtCardCost.Visible = visible;
		}
	}

	public void SetCardType(ECardType type)
	{
		if (_txtCardType == null)
		{
			return;
		}

		if (CardUiDefinitions.TryGetCardTypeLocaleKey(type, out var key))
		{
			_txtCardType.Text = Localization.Tr(key);
		}
		else
		{
			_txtCardType.Text = type.ToString();
		}

		_txtCardType.Visible = true;
	}

	public void SetBaseValue(int value)
	{
		_baseValue = value;
		if (_displayOverride is null)
		{
			RefreshValueLabel();
		}
	}

	public void SetElement(int elementFlags)
	{
		if (_crAttr == null)
		{
			return;
		}

		var colors = CardUiDefinitions.CollectElementColors(elementFlags);
		if (elementFlags != 0)
		{
			var bitCount = 0;
			foreach (EElement e in Enum.GetValues<EElement>())
			{
				if (e == EElement.None)
				{
					continue;
				}

				if ((elementFlags & (int)e) != 0)
				{
					bitCount++;
				}
			}

			if (bitCount > 3)
			{
				GD.PushWarning($"BaseCardItem: element flags truncated to 3 colors (flags={elementFlags}).");
			}
		}

		ApplyElementShader(colors);
		_crAttr.Visible = true;
	}

	public void SetArt(Texture2D? texture)
	{
		if (_trArt == null)
		{
			return;
		}

		_trArt.Texture = texture;
		_trArt.Visible = texture != null;
	}

	public void SetArtFromPath(string artPath, string? cardId = null)
	{
		if (string.IsNullOrWhiteSpace(artPath))
		{
			SetArt(null);
			return;
		}

		var texture = TryLoadArtTexture(artPath, cardId ?? _card?.Id);
		SetArt(texture);
	}

	public void SetCardFrame(Texture2D? texture)
	{
		if (_trCardFrame == null)
		{
			return;
		}

		_trCardFrame.Texture = texture;
		_trCardFrame.Visible = texture != null;
	}

	public void SetCardFrameFromRarity(ERarity rarity)
	{
		if (!CardUiDefinitions.TryGetCardFramePath(rarity, out var path))
		{
			SetCardFrame(null);
			return;
		}

		if (!ResourceLoader.Exists(path))
		{
			SetCardFrame(null);
			return;
		}

		var texture = ResourceLoader.Load<Texture2D>(path);
		SetCardFrame(texture);
	}

	#endregion

	#region 内部刷新

	private void ClearVisuals()
	{
		if (_txtCardCost != null)
		{
			_txtCardCost.Text = "";
			_txtCardCost.Visible = false;
		}

		if (_txtCardType != null)
		{
			_txtCardType.Text = "";
		}

		if (_txtCardVal != null)
		{
			_txtCardVal.Text = "";
		}

		ApplyElementShader([ColorDefinitions.NoneElement]);
		SetArt(null);
		SetCardFrame(null);
	}

	private void ApplyCost(ECostType costType, int cost)
	{
		_costType = costType;
		if (_txtCardCost == null)
		{
			return;
		}

		if (costType == ECostType.None)
		{
			_txtCardCost.Text = "";
			_txtCardCost.Visible = false;
			return;
		}

		_txtCardCost.Text = CardUiDefinitions.FormatCost(costType, cost);
		_txtCardCost.Visible = true;
	}

	private void RefreshValueLabel()
	{
		if (_txtCardVal == null)
		{
			return;
		}

		var value = _displayOverride ?? _baseValue;
		_txtCardVal.Text = value.ToString();
	}

	private void ApplyElementShader(Color[] colors)
	{
		if (_crAttr?.Material is not ShaderMaterial mat)
		{
			return;
		}

		var packed = new Vector4[3];
		for (var i = 0; i < 3; i++)
		{
			if (i < colors.Length)
			{
				var c = colors[i];
				packed[i] = new Vector4(c.R, c.G, c.B, c.A);
			}
			else
			{
				packed[i] = Vector4.Zero;
			}
		}

		mat.SetShaderParameter("colors", packed);
		mat.SetShaderParameter("color_count", colors.Length);
	}

	private static Texture2D? TryLoadArtTexture(string artPath, string? cardId)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(cardId)
				&& TryResolveModArtFile(cardId, artPath, out var modFile)
				&& File.Exists(modFile))
			{
				var image = Image.LoadFromFile(modFile);
				if (image != null)
				{
					return ImageTexture.CreateFromImage(image);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PushWarning($"BaseCardItem: mod art load failed: {ex.Message}");
		}

		var resPath = $"res://Resource/Assets/{artPath.Replace('\\', '/')}";
		try
		{
			if (ResourceLoader.Exists(resPath))
			{
				return ResourceLoader.Load<Texture2D>(resPath);
			}
		}
		catch (Exception ex)
		{
			GD.PushWarning($"BaseCardItem: resource art load failed: {ex.Message}");
		}

		return null;
	}

	private static bool TryResolveModArtFile(string cardId, string artPath, out string fullPath)
	{
		fullPath = "";
		try
		{
			var pipeline = AppRoot.Services.ContentModPipeline;
			if (!pipeline.Registry.TryGetOwnerModId(EContentCategory.Card, cardId, out var modId))
			{
				return false;
			}

			if (!pipeline.ScriptCatalog.TryGetContentRootPath(modId, out var contentRoot))
			{
				return false;
			}

			var relative = artPath.Replace('/', Path.DirectorySeparatorChar);
			fullPath = Path.GetFullPath(Path.Combine(contentRoot, relative));
			var rootFull = Path.GetFullPath(contentRoot);
			if (!fullPath.StartsWith(rootFull, OperatingSystem.IsWindows()
					? StringComparison.OrdinalIgnoreCase
					: StringComparison.Ordinal))
			{
				fullPath = "";
				return false;
			}

			return true;
		}
		catch (InvalidOperationException)
		{
			// AppRoot 未初始化
			return false;
		}
	}

	#endregion
}
```

注意：文件顶部需 `using KemoCard.Mod;` 以使用 `AppRoot`（若全局 using 未覆盖）。

- [ ] **Step 2: 编译项目**

```powershell
dotnet build kemo_card.csproj
```

Expected: 成功。若 `AppRoot` 命名空间报错，补 `using KemoCard.Mod;`。

- [ ] **Step 3: Commit**

```powershell
git add Src/mod/global/Ui/Comp/BaseCardItem.cs
git commit -m "feat(ui): 实现 BaseCardItem 数据绑定与分项刷新接口"
```

---

### Task 7: 场景 node_paths 与占位清理

**Files:**
- Modify: `Src/mod/global/Ui/Comp/BaseCardItem.tscn`

- [ ] **Step 1: 根节点绑定 Export**

将根节点改为（保留现有 `unique_id` / layout 属性，仅补 `node_paths` 与赋值）：

```
[node name="BaseCardItem" type="Control" ... node_paths=PackedStringArray("_trArt", "_trCardFrame", "_txtCardCost", "_txtCardType", "_txtCardVal", "_crAttr")]
...
script = ExtResource("1_o0l0o")
_trArt = NodePath("CArtRoot/TRArt")
_trCardFrame = NodePath("TRCardFrame")
_txtCardCost = NodePath("TCost")
_txtCardType = NodePath("TType")
_txtCardVal = NodePath("TCardVal")
_crAttr = NodePath("CRAttr")
```

- [ ] **Step 2: 清理占位文案**

- `TCost`：`text = ""`
- `TType`：`text = ""`
- `TCardVal`：`text = ""`

- [ ] **Step 3: Commit**

```powershell
git add Src/mod/global/Ui/Comp/BaseCardItem.tscn
git commit -m "chore(ui): BaseCardItem 场景绑定 Export 并清理占位文案"
```

---

### Task 8: 回归验证

**Files:** 无新增

- [ ] **Step 1: 跑相关单测**

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~CardDto_round_trips|FullyQualifiedName~CardUiDefinitionsTests|FullyQualifiedName~ModScriptCatalogTests|FullyQualifiedName~ModScriptLoaderTests"
```

Expected: 全部 PASS

- [ ] **Step 2: 全量 UI 测试（可选但推荐）**

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj
```

Expected: PASS（无新增失败）

- [ ] **Step 3: 若有未提交改动则提交测试修复**

仅当 Step 1/2 暴露需修问题时再 commit；否则本任务无额外 commit。

---

## Spec 覆盖自检

| Spec 条目 | 对应 Task |
|-----------|-----------|
| `CardDto.baseValue` | Task 1 |
| Definitions 类型键 / 卡框 / 元素色 | Task 2 |
| `strings.csv` `UI_CARD_TYPE_*` | Task 3 |
| `SetData` / `SetDisplayValue` + 分项 API | Task 6 |
| 费用 None/X/数字规则 | Task 2 `FormatCost` + Task 6 |
| 立绘 Mod→Resource 回退 | Task 4–6 |
| Catalog content 根 + Pipeline 暴露 | Task 4–5 |
| 元素色环最多 3 色 / None 回退 | Task 2 + Task 6 |
| 场景 node_paths / 占位清理 | Task 7 |
| 缺资源隐藏、AppRoot 未初始化不抛 | Task 6 |
| 纯逻辑单测 | Task 1–2、4、8 |
| 不改 CodexDlg / 不提交美术资源 | 全计划未触及 |
