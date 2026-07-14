# 设置窗口与 AlertDlg Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 落地静态编排的 `SettingDlg`（11 项设置）、三种可复用设置行组件、`BaseCmp` 订阅生命周期、`AlertDlg` 倒计时确认，以及显示/语言 Settings 的解析、运行时应用与启动恢复。

**Architecture:** frame 层提供可单测的键常量、注册表、解析器与 Display/Locale 应用入口；mod 层提供行组件场景与 `SettingDlg`/`AlertDlg`（`BaseDlg`）。显示模式/分辨率经 Alert 确认后立刻写盘；其余项立即生效、关设置窗写盘。布局在 Godot 编辑器完成，C# 只写逻辑。

**Tech Stack:** Godot 4.6 Mono / C# / NUnit / `DisplayServer` / `Window` / `Engine.MaxFps` / `TranslationServer` / 既有 `Sound` + `GlobalSave`

**Spec:** `Doc/superpowers/specs/2026-07-14-settings-dialog-design.md`

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `Src/frame/ui/Base/BaseUI.cs` | 扩展：通用 `Bind` 订阅表，并入 `ClearLifeCycle` |
| `Src/frame/ui/Base/BaseCmp.cs` | `UIId`/`UIDir` 默认、`InitEvent`、`OnUnbind` |
| `Src/frame/display/DisplaySettingKeys.cs` | 显示相关 Settings 键与默认值 |
| `Src/frame/display/WindowModeIds.cs` | `windowed` 等稳定字符串常量 |
| `Src/frame/display/ResolutionRegistry.cs` | 可扩展分辨率注册表 + 内置四档 |
| `Src/frame/display/DisplaySettingsParser.cs` | 字典 → 强类型状态（纯逻辑） |
| `Src/frame/display/DisplaySettingsApplier.cs` | 将状态应用到 `Window` / VSync / MaxFps |
| `Src/frame/locale/LocaleSettingKeys.cs` | `locale.language` 键与默认 |
| `Src/frame/locale/LocaleRegistry.cs` | 可扩展语言注册表 + zh_CN/en |
| `Src/frame/locale/LocaleSettingsApplier.cs` | `TranslationServer.SetLocale` |
| `Src/frame/audio/MuteFlagBits.cs` | mute_flag 按位读改（纯逻辑） |
| `Src/mod/global/Ui/Comp/SettingToggleRow.{cs,tscn}` | 滑块开关行 |
| `Src/mod/global/Ui/Comp/SettingDropdownRow.{cs,tscn}` | 下拉行 |
| `Src/mod/global/Ui/Comp/SettingSliderRow.{cs,tscn}` | 滑条行 |
| `Src/mod/global/Ui/AlertDlg.{cs,tscn}` | 通用确认框 + payload |
| `Src/mod/global/Ui/AlertDlgPayload.cs` | payload 与 `AlertClosePolicy` |
| `Src/mod/global/Ui/SettingDlg.{cs,tscn}` | 设置窗逻辑与静态行绑定 |
| `Src/mod/global/Ui/GlobalUiIds.cs` | 增加 Setting / Alert |
| `Src/mod/global/GlobalMod.cs` | 注册 Setting / Alert |
| `Src/mod/global/GlobalModController.cs` | `OpenSettingAsync` / `OpenAlertAsync` |
| `Src/mod/global/Ui/MenuWin.cs` | SettingsBtn 打开设置 |
| `Src/MainRoot.cs` | 启动应用显示 + 语言 |
| `Resource/Locale/strings.csv` | 设置/Alert 文案键 |
| `Tests/kemo_card.Ui.Tests/ResolutionRegistryTests.cs` | 注册表 |
| `Tests/kemo_card.Ui.Tests/LocaleRegistryTests.cs` | 注册表 |
| `Tests/kemo_card.Ui.Tests/DisplaySettingsParserTests.cs` | 解析与默认 |
| `Tests/kemo_card.Ui.Tests/MuteFlagBitsTests.cs` | 位操作 |

**布局约定：** `.tscn` 节点树在 Godot 编辑器搭建（可用 Godot MCP）；计划中的「节点树」是规格说明，禁止用 C# 动态拼控件布局。

**格式化：** 每个改完的 `.cs` 执行  
`dotnet format kemo_card.csproj --include <path>`  
（测试：`dotnet format Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --include <path>`）。

**提交信息：** 简体中文。

---

### Task 1: MuteFlagBits（TDD）

**Files:**
- Create: `Src/frame/audio/MuteFlagBits.cs`
- Create: `Tests/kemo_card.Ui.Tests/MuteFlagBitsTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using KemoCard.Frame.Audio;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class MuteFlagBitsTests
{
	[Test]
	public void Set_and_IsMuted_round_trip()
	{
		var flag = 0;
		flag = MuteFlagBits.WithMuted(flag, SoundBus.Master, true);
		flag = MuteFlagBits.WithMuted(flag, SoundBus.Sfx, true);
		Assert.That(MuteFlagBits.IsMuted(flag, SoundBus.Master), Is.True);
		Assert.That(MuteFlagBits.IsMuted(flag, SoundBus.Sound), Is.False);
		Assert.That(MuteFlagBits.IsMuted(flag, SoundBus.Sfx), Is.True);
		Assert.That(flag, Is.EqualTo((1 << SoundBus.Master) | (1 << SoundBus.Sfx)));
	}

	[Test]
	public void WithMuted_false_clears_bit()
	{
		var flag = MuteFlagBits.WithMuted(0, SoundBus.Sound, true);
		flag = MuteFlagBits.WithMuted(flag, SoundBus.Sound, false);
		Assert.That(flag, Is.EqualTo(0));
	}
}
```

- [ ] **Step 2: 运行确认失败**

```bash
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~MuteFlagBitsTests" -v n
```

Expected: 编译失败（缺少类型）。

- [ ] **Step 3: 实现**

```csharp
namespace KemoCard.Frame.Audio;

public static class MuteFlagBits
{
	public static bool IsMuted(int muteFlag, int busIndex) =>
		(muteFlag & (1 << busIndex)) != 0;

	public static int WithMuted(int muteFlag, int busIndex, bool muted) =>
		muted ? muteFlag | (1 << busIndex) : muteFlag & ~(1 << busIndex);
}
```

- [ ] **Step 4: 测试通过并格式化、提交**

```bash
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~MuteFlagBitsTests" -v n
dotnet format kemo_card.csproj --include Src/frame/audio/MuteFlagBits.cs
dotnet format Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --include Tests/kemo_card.Ui.Tests/MuteFlagBitsTests.cs
git add Src/frame/audio/MuteFlagBits.cs Tests/kemo_card.Ui.Tests/MuteFlagBitsTests.cs
git commit -m "feat(audio): 新增 MuteFlagBits 按位读写辅助"
```

---

### Task 2: ResolutionRegistry + LocaleRegistry（TDD）

**Files:**
- Create: `Src/frame/display/ResolutionRegistry.cs`
- Create: `Src/frame/locale/LocaleRegistry.cs`
- Create: `Tests/kemo_card.Ui.Tests/ResolutionRegistryTests.cs`
- Create: `Tests/kemo_card.Ui.Tests/LocaleRegistryTests.cs`

- [ ] **Step 1: 写失败测试**

`ResolutionRegistryTests.cs`：

```csharp
using System.Linq;
using KemoCard.Frame.Display;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ResolutionRegistryTests
{
	[SetUp]
	public void SetUp() => ResolutionRegistry.ResetToBuiltinsForTests();

	[Test]
	public void Builtins_four_entries_labeled_as_wxh()
	{
		var all = ResolutionRegistry.All;
		Assert.That(all.Select(e => e.Id), Is.EqualTo(new[]
		{
			"1280x720", "1920x1080", "2560x1440", "3840x2160"
		}));
		Assert.That(all[0].Width, Is.EqualTo(1280));
		Assert.That(all[0].DisplayLabel, Is.EqualTo("1280x720"));
	}

	[Test]
	public void Register_appends_custom_entry()
	{
		ResolutionRegistry.Register(new ResolutionEntry("1600x900", 1600, 900));
		Assert.That(ResolutionRegistry.TryGet("1600x900", out var e), Is.True);
		Assert.That(e.Height, Is.EqualTo(900));
	}
}
```

`LocaleRegistryTests.cs`：

```csharp
using System.Linq;
using KemoCard.Frame.Locale;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class LocaleRegistryTests
{
	[SetUp]
	public void SetUp() => LocaleRegistry.ResetToBuiltinsForTests();

	[Test]
	public void Builtins_zh_and_en()
	{
		Assert.That(LocaleRegistry.All.Select(e => e.Code), Is.EqualTo(new[] { "zh_CN", "en" }));
	}

	[Test]
	public void Register_appends_locale()
	{
		LocaleRegistry.Register(new LocaleEntry("ja", "UI_LOCALE_JA"));
		Assert.That(LocaleRegistry.TryGet("ja", out var e), Is.True);
		Assert.That(e.DisplayNameKey, Is.EqualTo("UI_LOCALE_JA"));
	}
}
```

- [ ] **Step 2: 运行确认失败**

```bash
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~ResolutionRegistryTests|FullyQualifiedName~LocaleRegistryTests" -v n
```

- [ ] **Step 3: 实现注册表**

`ResolutionEntry` / `ResolutionRegistry`：

```csharp
namespace KemoCard.Frame.Display;

public readonly record struct ResolutionEntry(string Id, int Width, int Height)
{
	public string DisplayLabel => $"{Width}x{Height}";
}

public static class ResolutionRegistry
{
	private static readonly List<ResolutionEntry> Entries = [];
	private static readonly object Gate = new();

	static ResolutionRegistry() => ResetToBuiltinsForTests();

	public static IReadOnlyList<ResolutionEntry> All
	{
		get { lock (Gate) return Entries.ToArray(); }
	}

	public static void Register(ResolutionEntry entry)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);
		lock (Gate)
		{
			if (Entries.Any(e => e.Id == entry.Id))
				return;
			Entries.Add(entry);
		}
	}

	public static bool TryGet(string id, out ResolutionEntry entry)
	{
		lock (Gate)
		{
			foreach (var e in Entries)
			{
				if (e.Id == id)
				{
					entry = e;
					return true;
				}
			}
		}
		entry = default;
		return false;
	}

	public static void ResetToBuiltinsForTests()
	{
		lock (Gate)
		{
			Entries.Clear();
			Entries.Add(new ResolutionEntry("1280x720", 1280, 720));
			Entries.Add(new ResolutionEntry("1920x1080", 1920, 1080));
			Entries.Add(new ResolutionEntry("2560x1440", 2560, 1440));
			Entries.Add(new ResolutionEntry("3840x2160", 3840, 2160));
		}
	}
}
```

`LocaleEntry` / `LocaleRegistry` 同理：内置 `("zh_CN","UI_LOCALE_ZH_CN")`、`("en","UI_LOCALE_EN")`，提供 `Register` / `TryGet` / `All` / `ResetToBuiltinsForTests`。

- [ ] **Step 4: 测试通过、格式化、提交**

```bash
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~ResolutionRegistryTests|FullyQualifiedName~LocaleRegistryTests" -v n
git add Src/frame/display/ResolutionRegistry.cs Src/frame/locale/LocaleRegistry.cs Tests/kemo_card.Ui.Tests/ResolutionRegistryTests.cs Tests/kemo_card.Ui.Tests/LocaleRegistryTests.cs
git commit -m "feat: 新增分辨率与语言可扩展注册表"
```

---

### Task 3: DisplaySettingKeys + DisplaySettingsParser（TDD）

**Files:**
- Create: `Src/frame/display/DisplaySettingKeys.cs`
- Create: `Src/frame/display/WindowModeIds.cs`
- Create: `Src/frame/display/DisplaySettingsParser.cs`
- Create: `Src/frame/locale/LocaleSettingKeys.cs`
- Create: `Tests/kemo_card.Ui.Tests/DisplaySettingsParserTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Collections.Generic;
using KemoCard.Frame.Display;
using KemoCard.Frame.Locale;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class DisplaySettingsParserTests
{
	[SetUp]
	public void SetUp() => ResolutionRegistry.ResetToBuiltinsForTests();

	[Test]
	public void Parse_uses_defaults_when_missing()
	{
		var state = DisplaySettingsParser.Parse(new Dictionary<string, string>());
		Assert.That(state.WindowMode, Is.EqualTo(WindowModeIds.Windowed));
		Assert.That(state.ResolutionId, Is.EqualTo(DisplaySettingKeys.DefaultResolutionId));
		Assert.That(state.VSync, Is.True);
		Assert.That(state.MaxFps, Is.EqualTo(60));
		Assert.That(state.LanguageCode, Is.EqualTo(LocaleSettingKeys.DefaultLanguage));
	}

	[Test]
	public void Parse_reads_valid_values()
	{
		var state = DisplaySettingsParser.Parse(new Dictionary<string, string>
		{
			[DisplaySettingKeys.WindowMode] = WindowModeIds.Fullscreen,
			[DisplaySettingKeys.Resolution] = "2560x1440",
			[DisplaySettingKeys.VSync] = "0",
			[DisplaySettingKeys.MaxFps] = "144",
			[LocaleSettingKeys.Language] = "en",
		});
		Assert.That(state.WindowMode, Is.EqualTo(WindowModeIds.Fullscreen));
		Assert.That(state.ResolutionId, Is.EqualTo("2560x1440"));
		Assert.That(state.VSync, Is.False);
		Assert.That(state.MaxFps, Is.EqualTo(144));
		Assert.That(state.LanguageCode, Is.EqualTo("en"));
	}

	[Test]
	public void Parse_falls_back_on_invalid()
	{
		var state = DisplaySettingsParser.Parse(new Dictionary<string, string>
		{
			[DisplaySettingKeys.WindowMode] = "nope",
			[DisplaySettingKeys.Resolution] = "1x1",
			[DisplaySettingKeys.VSync] = "x",
			[DisplaySettingKeys.MaxFps] = "-3",
			[LocaleSettingKeys.Language] = "fr",
		});
		Assert.That(state.WindowMode, Is.EqualTo(WindowModeIds.Windowed));
		Assert.That(state.ResolutionId, Is.EqualTo(DisplaySettingKeys.DefaultResolutionId));
		Assert.That(state.VSync, Is.True);
		Assert.That(state.MaxFps, Is.EqualTo(60));
		Assert.That(state.LanguageCode, Is.EqualTo(LocaleSettingKeys.DefaultLanguage));
	}

	[Test]
	public void Parse_allows_unlimited_fps_zero()
	{
		var state = DisplaySettingsParser.Parse(new Dictionary<string, string>
		{
			[DisplaySettingKeys.MaxFps] = "0",
		});
		Assert.That(state.MaxFps, Is.EqualTo(0));
	}
}
```

- [ ] **Step 2: 运行确认失败**

```bash
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~DisplaySettingsParserTests" -v n
```

- [ ] **Step 3: 实现键与解析器**

`WindowModeIds`：`Windowed`、`BorderlessWindow`、`BorderlessFullscreen`、`Fullscreen` 四个字符串常量，以及 `IsKnown(string)`。

`DisplaySettingKeys`：

```csharp
namespace KemoCard.Frame.Display;

public static class DisplaySettingKeys
{
	public const string WindowMode = "display.window_mode";
	public const string Resolution = "display.resolution";
	public const string VSync = "display.vsync";
	public const string MaxFps = "display.max_fps";
	public const string DefaultResolutionId = "1920x1080";
	public const int DefaultMaxFps = 60;
	public const bool DefaultVSync = true;
}
```

`LocaleSettingKeys`：`Language = "locale.language"`，`DefaultLanguage = "zh_CN"`。

`DisplaySettingsState` record：`WindowMode, ResolutionId, VSync, MaxFps, LanguageCode`。

`DisplaySettingsParser.Parse`：
- window_mode：未知 → `Windowed`
- resolution：`ResolutionRegistry.TryGet` 失败 → 默认 id
- vsync：`"1"`/`"true"`/`"yes"`（忽略大小写）为 true；`"0"`/`"false"`/`"no"` 为 false；否则默认 true
- max_fps：成功解析且 `>= 0` 则用该值（含 0）；否则默认 60。允许集合校验可选：若值不在 `{0,30,60,120,144,165,244}` 仍接受任意 `>=0` 整数（设置 UI 只给出这些选项即可）
- language：`LocaleRegistry.TryGet` 失败 → 默认

- [ ] **Step 4: 测试通过、格式化、提交**

```bash
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~DisplaySettingsParserTests" -v n
git commit -m "feat(display): 新增显示/语言 Settings 键与解析器"
```

---

### Task 4: DisplaySettingsApplier + LocaleSettingsApplier

**Files:**
- Create: `Src/frame/display/DisplaySettingsApplier.cs`
- Create: `Src/frame/locale/LocaleSettingsApplier.cs`
- Modify: `Src/MainRoot.cs`

- [ ] **Step 1: 实现 DisplaySettingsApplier**

```csharp
using Godot;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.Display;

public static class DisplaySettingsApplier
{
	public static void Apply(Window window, DisplaySettingsState state)
	{
		ArgumentNullException.ThrowIfNull(window);
		ApplyWindowMode(window, state.WindowMode);
		ApplyResolution(window, state.ResolutionId);
		DisplayServer.WindowSetVsyncMode(
			state.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
		Engine.MaxFps = state.MaxFps;
	}

	public static void ApplyWindowMode(Window window, string modeId)
	{
		window.Borderless = modeId is WindowModeIds.BorderlessWindow or WindowModeIds.BorderlessFullscreen;
		window.Mode = modeId switch
		{
			WindowModeIds.Fullscreen => Window.ModeEnum.ExclusiveFullscreen,
			WindowModeIds.BorderlessFullscreen => Window.ModeEnum.Fullscreen,
			WindowModeIds.BorderlessWindow => Window.ModeEnum.Windowed,
			_ => Window.ModeEnum.Windowed,
		};
	}

	public static void ApplyResolution(Window window, string resolutionId)
	{
		if (!ResolutionRegistry.TryGet(resolutionId, out var entry))
		{
			AppLog.Warning($"未知分辨率 {resolutionId}，回退默认", "DisplaySettings");
			if (!ResolutionRegistry.TryGet(DisplaySettingKeys.DefaultResolutionId, out entry))
				return;
		}
		window.Size = new Vector2I(entry.Width, entry.Height);
	}
}
```

（若目标平台上 `ExclusiveFullscreen` 行为异常，实现时以手测为准微调映射，但四种模式必须可区分。）

- [ ] **Step 2: 实现 LocaleSettingsApplier**

```csharp
using Godot;

namespace KemoCard.Frame.Locale;

public static class LocaleSettingsApplier
{
	public static void Apply(string languageCode)
	{
		if (!LocaleRegistry.TryGet(languageCode, out _))
			languageCode = LocaleSettingKeys.DefaultLanguage;
		TranslationServer.SetLocale(languageCode);
	}
}
```

- [ ] **Step 3: MainRoot 启动恢复**

在 `InitSoundManager` 之后（或同一 Settings 快照处）增加：

```csharp
var settings = AppRoot.Services.GlobalController.Snapshot.Settings;
var displayState = DisplaySettingsParser.Parse(settings);
DisplaySettingsApplier.Apply(GetWindow(), displayState);
LocaleSettingsApplier.Apply(displayState.LanguageCode);
```

注意：`AudioSettingsLoader.Apply` 已使用 settings；避免重复读时可抽局部变量一次读取。

- [ ] **Step 4: 格式化、提交**

```bash
dotnet format kemo_card.csproj --include Src/frame/display/DisplaySettingsApplier.cs Src/frame/locale/LocaleSettingsApplier.cs Src/MainRoot.cs
git add Src/frame/display/DisplaySettingsApplier.cs Src/frame/locale/LocaleSettingsApplier.cs Src/MainRoot.cs
git commit -m "feat: 启动时应用显示模式、分辨率、垂直同步、帧率与语言"
```

---

### Task 5: BaseUI.Bind + BaseCmp 补强

**Files:**
- Modify: `Src/frame/ui/Base/BaseUI.cs`
- Modify: `Src/frame/ui/Base/BaseCmp.cs`

- [ ] **Step 1: BaseUI 增加通用 Bind**

在 `BaseUI` 中增加：

```csharp
private readonly List<Action> _unbindActions = [];

protected void Bind(Action subscribe, Action unsubscribe)
{
	ArgumentNullException.ThrowIfNull(subscribe);
	ArgumentNullException.ThrowIfNull(unsubscribe);
	subscribe();
	_unbindActions.Add(unsubscribe);
}
```

在 `ClearLifeCycle` 末尾：

```csharp
for (var i = _unbindActions.Count - 1; i >= 0; i--)
{
	try { _unbindActions[i](); }
	catch { /* 离开树时忽略反订阅异常 */ }
}
_unbindActions.Clear();
```

- [ ] **Step 2: 完善 BaseCmp**

```csharp
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

public abstract partial class BaseCmp : BaseUI
{
	public override EUIType UIType => EUIType.Cmp;
	public override string UIId => GetType().Name;
	public override string UIDir => string.Empty;

	protected override void OnReady()
	{
		base.OnReady();
		InitEvent();
	}

	public override void _ExitTree()
	{
		OnUnbind();
		base._ExitTree();
	}

	protected virtual void InitEvent() { }
	protected virtual void OnUnbind() { }
}
```

注意：`base._ExitTree()` 会调 `ClearLifeCycle`；`OnUnbind` 放在之前以便先 Kill Tween，再卸信号。

- [ ] **Step 3: 编译确认、格式化、提交**

```bash
dotnet build kemo_card.csproj -v q
git commit -m "feat(ui): BaseCmp 生命周期与 BaseUI.Bind 通用反订阅"
```

---

### Task 6: 三个设置行组件（场景 + 逻辑）

**Files:**
- Create: `Src/mod/global/Ui/Comp/SettingToggleRow.{cs,tscn}`
- Create: `Src/mod/global/Ui/Comp/SettingDropdownRow.{cs,tscn}`
- Create: `Src/mod/global/Ui/Comp/SettingSliderRow.{cs,tscn}`

**节点树（Godot 编辑器搭建，根挂对应脚本）：**

共同结构：

```
Root (HBoxContainer)  ← 脚本
├─ NameLabel (Label)           size flags: fill
├─ Spacer (Control)            size flags: expand + fill
└─ <控件>
```

- Toggle：`Track` (Panel/ColorRect，固定宽高约 56×28) + 子节点 `Knob` (ColorRect 圆角方块)；整行或 Track 可点。
- Dropdown：`OptionButton`
- Slider：`HSlider`（可选旁路 `ValueLabel`）

- [ ] **Step 1: SettingToggleRow 逻辑要点**

```csharp
namespace KemoCard.Mod.Global.Ui.Comp;

public partial class SettingToggleRow : BaseCmp
{
	[Export] public Label? NameLabel { get; set; }
	[Export] public Control? Track { get; set; }
	[Export] public Control? Knob { get; set; }
	[Export] public bool DefaultValue { get; set; }
	[Export] public float AnimDuration { get; set; } = 0.12f;

	public event Action<bool>? ValueChanged;

	private bool _value;
	private Tween? _tween;
	private bool _suppress;

	public bool Value
	{
		get => _value;
		set => SetValue(value, animate: true, notify: true);
	}

	public void SetValue(bool value, bool animate, bool notify)
	{
		_value = value;
		PlayVisual(animate);
		if (notify && !_suppress)
			ValueChanged?.Invoke(_value);
	}

	public void SetNameKey(string key)
	{
		if (NameLabel != null)
			NameLabel.Text = key; // Godot 自动翻译：控件 text 填翻译键
	}

	protected override void InitEvent()
	{
		_suppress = true;
		SetValue(DefaultValue, animate: false, notify: false);
		_suppress = false;
		if (Track != null)
		{
			Bind(
				() => Track.GuiInput += OnTrackGuiInput,
				() => Track.GuiInput -= OnTrackGuiInput);
		}
	}

	protected override void OnUnbind()
	{
		_tween?.Kill();
		_tween = null;
	}

	private void OnTrackGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			SetValue(!_value, animate: true, notify: true);
	}

	private void PlayVisual(bool animate)
	{
		// Knob 目标 position：关靠左、开靠右；Track 调制色
		// 用 Tween 插值；animate=false 时直接设最终状态
	}
}
```

- [ ] **Step 2: SettingDropdownRow**

- `SetOptions(IReadOnlyList<(string id, string label)> options)`：清空后按序 `AddItem(label)`，内部平行数组存 id。
- `SelectedId` get/set；`ItemSelected` → `ValueChanged?.Invoke(id)`。
- `DefaultValue` 为默认 id；`InitEvent` 中选中默认（找不到则选 0）。
- 用 `Bind` 订阅 `OptionButton.ItemSelected`。

- [ ] **Step 3: SettingSliderRow**

- Export：`MinValue`/`MaxValue`/`Step`/`DefaultValue`。
- `Value` get/set；`HSlider.ValueChanged` → 事件。
- `InitEvent` 配置滑条范围并设默认值。

- [ ] **Step 4: 编辑器手测三项组件、格式化、提交**

```bash
dotnet format kemo_card.csproj --include Src/mod/global/Ui/Comp/SettingToggleRow.cs Src/mod/global/Ui/Comp/SettingDropdownRow.cs Src/mod/global/Ui/Comp/SettingSliderRow.cs
git add Src/mod/global/Ui/Comp/SettingToggleRow.cs Src/mod/global/Ui/Comp/SettingToggleRow.tscn Src/mod/global/Ui/Comp/SettingDropdownRow.cs Src/mod/global/Ui/Comp/SettingDropdownRow.tscn Src/mod/global/Ui/Comp/SettingSliderRow.cs Src/mod/global/Ui/Comp/SettingSliderRow.tscn
git commit -m "feat(ui): 新增设置行组件 Toggle/Dropdown/Slider"
```

---

### Task 7: AlertDlg

**Files:**
- Create: `Src/mod/global/Ui/AlertDlgPayload.cs`
- Create: `Src/mod/global/Ui/AlertDlg.{cs,tscn}`
- Modify: `Src/mod/global/Ui/GlobalUiIds.cs`
- Modify: `Src/mod/global/GlobalMod.cs`
- Modify: `Src/mod/global/GlobalModController.cs`
- Modify: `Resource/Locale/strings.csv`

- [ ] **Step 1: Payload**

```csharp
namespace KemoCard.Mod.Global.Ui;

public enum AlertClosePolicy
{
	Cancel = 0,
	Ok = 1,
	None = 2,
}

public sealed class AlertDlgPayload
{
	public string TitleKey { get; init; } = "UI_ALERT_TITLE";
	public string DescKey { get; init; } = "UI_ALERT_DESC";
	public string OkTextKey { get; init; } = "UI_ALERT_OK";
	public string CancelTextKey { get; init; } = "UI_ALERT_CANCEL";
	public Action? OkCallback { get; init; }
	public Action? CancelCallback { get; init; }
	public AlertClosePolicy CallbackWhenClose { get; init; } = AlertClosePolicy.Cancel;
	/// <summary>秒；≤0 表示无倒计时自动关闭。</summary>
	public double Time { get; init; } = 10;
}
```

- [ ] **Step 2: 场景（编辑器）**

基于 `BaseDlgComp`（与 Codex/Setting 一致）：标题 Label、描述 Label（可含倒计时占位）、OK/Cancel 按钮。脚本 `AlertDlg : BaseDlg`。

- [ ] **Step 3: AlertDlg 逻辑**

- `UIId => GlobalUiIds.Alert`，`UIDir => "Src/mod/global/Ui"`。
- `OnOpen`：读 `GetTypedPayload<AlertDlgPayload>()`，灌文案键，启动倒计时（`Time > 0` 时用 `SceneTreeTimer` 或 `_Process` 累减；UI 显示剩余整数秒）。
- OK → 调 `OkCallback`，设 `_handled=true`，`Close()`。
- Cancel → 调 `CancelCallback`，`_handled=true`，`Close()`。
- 超时 → 同 Cancel。
- `OnClose`：若 `!_handled`，按 `CallbackWhenClose` 调 Ok / Cancel / 跳过。
- 防重入：回调与关闭只触发一次。

- [ ] **Step 4: 注册与打开 API**

`GlobalUiIds`：`Setting = "SettingDlg"`，`Alert = "AlertDlg"`。  
`GetUIRegistrations`：增加两个 `UIRegistration.Dialog(...)`。  
`OpenAlertAsync(AlertDlgPayload payload)` / `OpenSettingAsync()` 对齐 `OpenCodexAsync`。

strings.csv 增加至少：`UI_ALERT_OK`、`UI_ALERT_CANCEL`、`UI_ALERT_DISPLAY_TITLE`、`UI_ALERT_DISPLAY_DESC`（描述可含说明「倒计时结束后将还原」；剩余秒数由代码拼到 Desc 或独立 Label，避免硬编码整句用户文案——数字可变部分可用 `string.Format`/`Tr` 后替换 `{0}`，键写在 csv）。

- [ ] **Step 5: 提交**

```bash
git commit -m "feat(ui): 新增 AlertDlg 倒计时确认框"
```

---

### Task 8: SettingDlg 场景静态排布 11 行

**Files:**
- Modify: `Src/mod/global/Ui/SettingDlg.tscn`
- Modify: `Src/mod/global/Ui/SettingDlg.cs`（先骨架 Export）

- [ ] **Step 1: 在 Godot 中向 `SettingVBoxContainer` 实例化行组件（顺序固定）**

1. `WindowModeRow` — SettingDropdownRow  
2. `ResolutionRow` — SettingDropdownRow  
3. `VSyncRow` — SettingToggleRow  
4. `MaxFpsRow` — SettingDropdownRow  
5. `MasterMuteRow` — SettingToggleRow  
6. `MasterVolumeRow` — SettingSliderRow（0–100）  
7. `MusicMuteRow` — SettingToggleRow  
8. `MusicVolumeRow` — SettingSliderRow  
9. `SfxMuteRow` — SettingToggleRow  
10. `SfxVolumeRow` — SettingSliderRow  
11. `LanguageRow` — SettingDropdownRow  

`ScrollContainer` 需允许纵向滚动；VBox `size flags` 横向填满。

- [ ] **Step 2: SettingDlg 改为 BaseDlg + Export 引用上述 11 行**

```csharp
namespace KemoCard.Mod.Global.Ui;

public partial class SettingDlg : BaseDlg
{
	[Export] public SettingDropdownRow? WindowModeRow { get; set; }
	// ... 其余行
	public override string UIId => GlobalUiIds.Setting;
	public override string UIDir => "Src/mod/global/Ui";
}
```

- [ ] **Step 3: 提交场景骨架**

```bash
git commit -m "feat(ui): SettingDlg 静态排布十一项设置行"
```

---

### Task 9: SettingDlg 绑定、确认流与关窗持久化

**Files:**
- Modify: `Src/mod/global/Ui/SettingDlg.cs`
- Modify: `Src/mod/global/Ui/MenuWin.cs`
- Modify: `Resource/Locale/strings.csv`

- [ ] **Step 1: 打开时灌值**

`OnOpen`：
- 取 `AppRoot.Services.GlobalController.Snapshot.Settings`（或项目既有获取 GlobalController 方式，与 Codex 一致）。
- `DisplaySettingsParser.Parse` → 填显示相关行。
- 音量：读 `AudioSettingKeys.*`；静音用 `MuteFlagBits`。
- 下拉选项：
  - 显示模式：四个 `WindowModeIds` + 本地化标签键
  - 分辨率：`ResolutionRegistry.All`，label=`DisplayLabel`
  - 最高帧率：`(30,"30")...(0, Tr("UI_SETTING_FPS_UNLIMITED"))`
  - 语言：`LocaleRegistry.All`，label 用 `DisplayNameKey`（控件 text 键）

- [ ] **Step 2: 非确认项立即应用**

- VSync / MaxFps → `DisplaySettingsApplier` 对应方法或局部 Apply。  
- 音量 / 静音 → `Sound.SetBusVolumePercent` / `Sound.SetMuteFlag(MuteFlagBits.WithMuted(...))`。  
- 语言 → `LocaleSettingsApplier.Apply`。

维护内存字段 `_pendingVSync`、`_pendingMaxFps`、音量、静音 flag、语言，供关窗写盘。

- [ ] **Step 3: 显示模式/分辨率确认流**

字段：`_displayConfirmBusy`、`_revertWindowMode`、`_revertResolution`、`_trialWindowMode`、`_trialResolution`。

变更回调：
1. 若已有确认流：先执行取消回退（关 Alert 若仍开）。
2. 保存 revert = 当前已确认/打开时值。
3. 试用 `DisplaySettingsApplier.ApplyWindowMode` / `ApplyResolution`。
4. `OpenAlertAsync`，payload：`Time=10`，OK → `SetSetting` 两项相关键 + `SaveToDisk`，更新「已确认」快照；Cancel/超时 → 回退显示与下拉 `SetValue(..., notify:false)`。

- [ ] **Step 4: OnClose 持久化 3–11**

```csharp
protected override void OnClose()
{
	CancelDisplayConfirmIfNeeded();
	var gc = AppRoot.Services.GlobalController;
	gc.SetSetting(DisplaySettingKeys.VSync, _pendingVSync ? "1" : "0");
	gc.SetSetting(DisplaySettingKeys.MaxFps, _pendingMaxFps.ToString());
	// master/sound/sfx volume + mute_flag + locale.language
	gc.SaveToDisk();
}
```

不要覆盖已确认的 window_mode / resolution（除非确认流取消已回退）。

- [ ] **Step 5: MenuWin 接线**

```csharp
if (SettingsBtn != null)
	OnClicks(SettingsBtn, () => _ = GlobalModController.OpenSettingAsync());
```

- [ ] **Step 6: 补全 strings.csv 设置名键**（`UI_SETTING_WINDOW_MODE`、`UI_SETTING_RESOLUTION`、`UI_SETTING_VSYNC`、`UI_SETTING_MAX_FPS`、三路音量/静音、语言、四种窗口模式、两种语言名、FPS 不限制等）。

- [ ] **Step 7: 编辑器手测清单后提交**

手测：
1. 菜单打开设置  
2. 拖音量立即有声变化，关窗重启仍在  
3. 改分辨率弹出 10s Alert，确认保留，取消/超时回退  
4. 改显示模式同上  
5. 切语言 UI 立即切换，关窗重启仍在  
6. VSync / MaxFps 立即生效并持久化  

```bash
git commit -m "feat(ui): 完成 SettingDlg 绑定、显示确认流与关窗持久化"
```

---

### Task 10: Spec 状态与收尾

**Files:**
- Modify: `Doc/superpowers/specs/2026-07-14-settings-dialog-design.md`（状态改为已实现）

- [ ] **Step 1: 跑相关单测**

```bash
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~MuteFlagBitsTests|FullyQualifiedName~ResolutionRegistryTests|FullyQualifiedName~LocaleRegistryTests|FullyQualifiedName~DisplaySettingsParserTests" -v n
```

Expected: 全部 PASS。

- [ ] **Step 2: 更新 spec 状态为「已实现」，提交**

```bash
git commit -m "docs: 将设置窗口设计规格标记为已实现"
```

---

## Self-Review（对照 Spec）

| Spec 要求 | 对应 Task |
|-----------|-----------|
| BaseCmp Bind/InitEvent/OnUnbind/默认 UIId | Task 5 |
| 三行组件 + 滑块动效 | Task 6 |
| 11 项静态列表 | Task 8–9 |
| AlertDlg payload / 10s / ClosePolicy | Task 7 |
| 显示确认立刻写盘 | Task 9 |
| 其余关窗写盘 + 立即生效 | Task 9 |
| 分辨率/语言注册表可扩展 | Task 2 |
| 分辨率展示 WxH | Task 2/6/9 |
| 启动恢复显示+语言 | Task 4 |
| MuteFlag 位 | Task 1/9 |
| 本地化 | Task 7/9 |
| 菜单入口 | Task 9 |

无 TBD 占位；类型名与 Task 间保持一致（`DisplaySettingsState`、`WindowModeIds`、`AlertClosePolicy`）。
