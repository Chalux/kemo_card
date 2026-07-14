# 音频管理器（Sound）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 frame 层落地 `ISoundService` / `Sound` 门面 / `SoundManager`（BGM+Ambient+SFX 池、总线音量与静音、LRU 缓存），并在 `MainRoot` 挂树、Configure、从全局存档 Settings 恢复。

**Architecture:** 对齐 `AppLog`：可测接口 + `NullSoundService` + 静态 `Sound.Configure`；Godot 实现为 `SoundManager : Node`（位于 `Src/frame/audio/`）。Settings 键常量与「字典 → 音量/静音」纯逻辑放 frame，便于单测；`MainRoot` 读 `AppRoot.Services.GlobalController` 后调用应用逻辑。`frame` 可用 Godot，不得依赖 `mod`。

**Tech Stack:** Godot 4.6 Mono / C# / NUnit / `AudioServer` + `AudioStreamPlayer`

**Spec:** `Doc/superpowers/specs/2026-07-14-sound-manager-design.md`

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `Src/frame/audio/SoundBus.cs` | 总线索引常量 Master/Sound/Sfx |
| `Src/frame/audio/ISoundService.cs` | 播放 / 音量 / 静音 / 清缓存接口 |
| `Src/frame/audio/NullSoundService.cs` | 空实现 + `Instance` |
| `Src/frame/audio/Sound.cs` | 静态门面 `Configure` + 转发 |
| `Src/frame/audio/AudioSettingKeys.cs` | Settings 键名与默认值常量 |
| `Src/frame/audio/AudioSettingsLoader.cs` | 从 `IReadOnlyDictionary<string,string>` 解析并应用到 `ISoundService`（纯逻辑，可单测） |
| `Src/frame/audio/SoundManager.cs` | Node：双路 Sound 播放器、SFX 池、LRU、AudioServer、静音渐变 |
| `Src/MainRoot.cs` | 创建挂树、`Sound.Configure`、应用 Settings、退出重置门面 |
| `default_bus_layout.tres` | 已有；纳入版本库（Master/Sound/SFX） |
| `Tests/kemo_card.Ui.Tests/RecordingSoundService.cs` | 测试假实现 |
| `Tests/kemo_card.Ui.Tests/SoundFacadeTests.cs` | 门面转发 / Null / Configure null |
| `Tests/kemo_card.Ui.Tests/AudioSettingsLoaderTests.cs` | Settings 解析与默认值 |

**不改：** 设置 UI、交叉淡入淡出、业务侧默认播菜单 BGM、mod 内容音频清单。

**格式化：** 每个 `.cs` 逻辑改完后对该文件执行 `dotnet format kemo_card.csproj --include <path>`（测试项目用 `Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj`）。

---

### Task 1: SoundBus + ISoundService + NullSoundService + Sound 门面（TDD）

**Files:**
- Create: `Src/frame/audio/SoundBus.cs`
- Create: `Src/frame/audio/ISoundService.cs`
- Create: `Src/frame/audio/NullSoundService.cs`
- Create: `Src/frame/audio/Sound.cs`
- Create: `Tests/kemo_card.Ui.Tests/RecordingSoundService.cs`
- Create: `Tests/kemo_card.Ui.Tests/SoundFacadeTests.cs`

- [ ] **Step 1: 写失败测试（RecordingSoundService + Sound 门面）**

`Tests/kemo_card.Ui.Tests/RecordingSoundService.cs`：

```csharp
using System.Collections.Generic;
using KemoCard.Frame.Audio;

namespace KemoCard.Ui.Tests;

public sealed class RecordingSoundService : ISoundService
{
	public List<string> Calls { get; } = new();

	public void PlayBgm(string resourcePath) => Calls.Add($"PlayBgm:{resourcePath}");
	public void StopBgm() => Calls.Add("StopBgm");
	public void PlayAmbient(string resourcePath) => Calls.Add($"PlayAmbient:{resourcePath}");
	public void StopAmbient() => Calls.Add("StopAmbient");
	public void PlaySfx(string resourcePath) => Calls.Add($"PlaySfx:{resourcePath}");

	public void SetBusVolumePercent(int busIndex, int volumePercent) =>
		Calls.Add($"SetBusVolumePercent:{busIndex}:{volumePercent}");

	public int GetBusVolumePercent(int busIndex)
	{
		Calls.Add($"GetBusVolumePercent:{busIndex}");
		return 100;
	}

	public void SetMuteFlag(int muteFlag) => Calls.Add($"SetMuteFlag:{muteFlag}");

	public int GetMuteFlag()
	{
		Calls.Add("GetMuteFlag");
		return 0;
	}

	public void ClearCache() => Calls.Add("ClearCache");
}
```

`Tests/kemo_card.Ui.Tests/SoundFacadeTests.cs`：

```csharp
using KemoCard.Frame.Audio;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class SoundFacadeTests
{
	[TearDown]
	public void TearDown()
	{
		Sound.Configure(NullSoundService.Instance);
	}

	[Test]
	public void Configure_forwards_calls_to_implementation()
	{
		var recording = new RecordingSoundService();
		Sound.Configure(recording);

		Sound.PlayBgm("res://a.ogg");
		Sound.StopBgm();
		Sound.PlayAmbient("res://b.ogg");
		Sound.StopAmbient();
		Sound.PlaySfx("res://c.ogg");
		Sound.SetBusVolumePercent(SoundBus.Master, 80);
		_ = Sound.GetBusVolumePercent(SoundBus.Sound);
		Sound.SetMuteFlag(1);
		_ = Sound.GetMuteFlag();
		Sound.ClearCache();

		Assert.That(recording.Calls, Is.EqualTo(new[]
		{
			"PlayBgm:res://a.ogg",
			"StopBgm",
			"PlayAmbient:res://b.ogg",
			"StopAmbient",
			"PlaySfx:res://c.ogg",
			"SetBusVolumePercent:0:80",
			"GetBusVolumePercent:1",
			"SetMuteFlag:1",
			"GetMuteFlag",
			"ClearCache",
		}));
	}

	[Test]
	public void Without_configure_does_not_throw()
	{
		Sound.Configure(NullSoundService.Instance);
		Assert.DoesNotThrow(() =>
		{
			Sound.PlayBgm("res://x.ogg");
			Sound.PlayAmbient("res://x.ogg");
			Sound.PlaySfx("res://x.ogg");
			Sound.SetBusVolumePercent(0, 50);
			_ = Sound.GetBusVolumePercent(0);
			Sound.SetMuteFlag(0);
			_ = Sound.GetMuteFlag();
			Sound.ClearCache();
		});
	}

	[Test]
	public void Configure_null_throws()
	{
		Assert.Throws<System.ArgumentNullException>(() => Sound.Configure(null!));
	}
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~SoundFacadeTests" -v n
```

Expected: 编译失败（缺少 `KemoCard.Frame.Audio` 类型）。

- [ ] **Step 3: 实现最小门面**

`Src/frame/audio/SoundBus.cs`：

```csharp
namespace KemoCard.Frame.Audio;

public static class SoundBus
{
	public const int Master = 0;
	public const int Sound = 1;
	public const int Sfx = 2;
	public const int Count = 3;
}
```

`Src/frame/audio/ISoundService.cs`：

```csharp
namespace KemoCard.Frame.Audio;

public interface ISoundService
{
	void PlayBgm(string resourcePath);
	void StopBgm();
	void PlayAmbient(string resourcePath);
	void StopAmbient();
	void PlaySfx(string resourcePath);

	void SetBusVolumePercent(int busIndex, int volumePercent);
	int GetBusVolumePercent(int busIndex);
	void SetMuteFlag(int muteFlag);
	int GetMuteFlag();

	void ClearCache();
}
```

`Src/frame/audio/NullSoundService.cs`：

```csharp
namespace KemoCard.Frame.Audio;

public sealed class NullSoundService : ISoundService
{
	public static NullSoundService Instance { get; } = new();

	public void PlayBgm(string resourcePath) { }
	public void StopBgm() { }
	public void PlayAmbient(string resourcePath) { }
	public void StopAmbient() { }
	public void PlaySfx(string resourcePath) { }

	public void SetBusVolumePercent(int busIndex, int volumePercent) { }

	public int GetBusVolumePercent(int busIndex) => 100;

	public void SetMuteFlag(int muteFlag) { }

	public int GetMuteFlag() => 0;

	public void ClearCache() { }
}
```

`Src/frame/audio/Sound.cs`：

```csharp
using System;

namespace KemoCard.Frame.Audio;

public static class Sound
{
	private static ISoundService _implementation = NullSoundService.Instance;

	public static void Configure(ISoundService implementation)
	{
		ArgumentNullException.ThrowIfNull(implementation);
		_implementation = implementation;
	}

	public static void PlayBgm(string resourcePath) => _implementation.PlayBgm(resourcePath);
	public static void StopBgm() => _implementation.StopBgm();
	public static void PlayAmbient(string resourcePath) => _implementation.PlayAmbient(resourcePath);
	public static void StopAmbient() => _implementation.StopAmbient();
	public static void PlaySfx(string resourcePath) => _implementation.PlaySfx(resourcePath);

	public static void SetBusVolumePercent(int busIndex, int volumePercent) =>
		_implementation.SetBusVolumePercent(busIndex, volumePercent);

	public static int GetBusVolumePercent(int busIndex) =>
		_implementation.GetBusVolumePercent(busIndex);

	public static void SetMuteFlag(int muteFlag) => _implementation.SetMuteFlag(muteFlag);

	public static int GetMuteFlag() => _implementation.GetMuteFlag();

	public static void ClearCache() => _implementation.ClearCache();
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: 同 Step 2。  
Expected: 全部 PASS。

- [ ] **Step 5: 格式化并提交**

```powershell
dotnet format kemo_card.csproj --include Src/frame/audio/SoundBus.cs Src/frame/audio/ISoundService.cs Src/frame/audio/NullSoundService.cs Src/frame/audio/Sound.cs
dotnet format Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --include Tests/kemo_card.Ui.Tests/RecordingSoundService.cs Tests/kemo_card.Ui.Tests/SoundFacadeTests.cs
git add Src/frame/audio/SoundBus.cs Src/frame/audio/ISoundService.cs Src/frame/audio/NullSoundService.cs Src/frame/audio/Sound.cs Tests/kemo_card.Ui.Tests/RecordingSoundService.cs Tests/kemo_card.Ui.Tests/SoundFacadeTests.cs
git commit -m "feat(audio): 新增 ISoundService 与 Sound 静态门面"
```

---

### Task 2: AudioSettingKeys + AudioSettingsLoader（TDD）

**Files:**
- Create: `Src/frame/audio/AudioSettingKeys.cs`
- Create: `Src/frame/audio/AudioSettingsLoader.cs`
- Create: `Tests/kemo_card.Ui.Tests/AudioSettingsLoaderTests.cs`

- [ ] **Step 1: 写失败测试**

`Tests/kemo_card.Ui.Tests/AudioSettingsLoaderTests.cs`：

```csharp
using System.Collections.Generic;
using KemoCard.Frame.Audio;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class AudioSettingsLoaderTests
{
	[Test]
	public void Apply_uses_defaults_when_keys_missing()
	{
		var recording = new RecordingSoundService();
		AudioSettingsLoader.Apply(recording, new Dictionary<string, string>());

		Assert.That(recording.Calls, Is.EqualTo(new[]
		{
			$"SetBusVolumePercent:{SoundBus.Master}:100",
			$"SetBusVolumePercent:{SoundBus.Sound}:100",
			$"SetBusVolumePercent:{SoundBus.Sfx}:100",
			"SetMuteFlag:0",
		}));
	}

	[Test]
	public void Apply_reads_valid_values()
	{
		var recording = new RecordingSoundService();
		AudioSettingsLoader.Apply(recording, new Dictionary<string, string>
		{
			[AudioSettingKeys.MasterVolume] = "80",
			[AudioSettingKeys.SoundVolume] = "60",
			[AudioSettingKeys.SfxVolume] = "40",
			[AudioSettingKeys.MuteFlag] = "5",
		});

		Assert.That(recording.Calls, Is.EqualTo(new[]
		{
			$"SetBusVolumePercent:{SoundBus.Master}:80",
			$"SetBusVolumePercent:{SoundBus.Sound}:60",
			$"SetBusVolumePercent:{SoundBus.Sfx}:40",
			"SetMuteFlag:5",
		}));
	}

	[Test]
	public void Apply_falls_back_on_invalid_numbers()
	{
		var recording = new RecordingSoundService();
		AudioSettingsLoader.Apply(recording, new Dictionary<string, string>
		{
			[AudioSettingKeys.MasterVolume] = "nope",
			[AudioSettingKeys.SoundVolume] = "",
			[AudioSettingKeys.SfxVolume] = "999",
			[AudioSettingKeys.MuteFlag] = "x",
		});

		// 音量非法 → 默认 100；999 由 SetBusVolumePercent 负责 clamp，Loader 仍传入解析到的 int 999
		// Mute 非法 → 0。约定：TryParse 失败用默认；解析成功则原样传给 service（clamp 在 SoundManager）。
		Assert.That(recording.Calls, Is.EqualTo(new[]
		{
			$"SetBusVolumePercent:{SoundBus.Master}:100",
			$"SetBusVolumePercent:{SoundBus.Sound}:100",
			$"SetBusVolumePercent:{SoundBus.Sfx}:999",
			"SetMuteFlag:0",
		}));
	}
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~AudioSettingsLoaderTests" -v n
```

Expected: 编译失败（缺少 `AudioSettingsLoader` / `AudioSettingKeys`）。

- [ ] **Step 3: 实现**

`Src/frame/audio/AudioSettingKeys.cs`：

```csharp
namespace KemoCard.Frame.Audio;

public static class AudioSettingKeys
{
	public const string MasterVolume = "audio.master_volume";
	public const string SoundVolume = "audio.sound_volume";
	public const string SfxVolume = "audio.sfx_volume";
	public const string MuteFlag = "audio.mute_flag";

	public const int DefaultVolumePercent = 100;
	public const int DefaultMuteFlag = 0;
}
```

`Src/frame/audio/AudioSettingsLoader.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace KemoCard.Frame.Audio;

public static class AudioSettingsLoader
{
	public static void Apply(ISoundService sound, IReadOnlyDictionary<string, string> settings)
	{
		ArgumentNullException.ThrowIfNull(sound);
		ArgumentNullException.ThrowIfNull(settings);

		sound.SetBusVolumePercent(SoundBus.Master, ReadInt(settings, AudioSettingKeys.MasterVolume, AudioSettingKeys.DefaultVolumePercent));
		sound.SetBusVolumePercent(SoundBus.Sound, ReadInt(settings, AudioSettingKeys.SoundVolume, AudioSettingKeys.DefaultVolumePercent));
		sound.SetBusVolumePercent(SoundBus.Sfx, ReadInt(settings, AudioSettingKeys.SfxVolume, AudioSettingKeys.DefaultVolumePercent));
		sound.SetMuteFlag(ReadInt(settings, AudioSettingKeys.MuteFlag, AudioSettingKeys.DefaultMuteFlag));
	}

	private static int ReadInt(IReadOnlyDictionary<string, string> settings, string key, int defaultValue)
	{
		if (!settings.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
		{
			return defaultValue;
		}

		return int.TryParse(raw, out var value) ? value : defaultValue;
	}
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: 同 Step 2。Expected: PASS。

- [ ] **Step 5: 格式化并提交**

```powershell
dotnet format kemo_card.csproj --include Src/frame/audio/AudioSettingKeys.cs Src/frame/audio/AudioSettingsLoader.cs
dotnet format Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --include Tests/kemo_card.Ui.Tests/AudioSettingsLoaderTests.cs
git add Src/frame/audio/AudioSettingKeys.cs Src/frame/audio/AudioSettingsLoader.cs Tests/kemo_card.Ui.Tests/AudioSettingsLoaderTests.cs
git commit -m "feat(audio): 新增 Settings 键常量与 AudioSettingsLoader"
```

---

### Task 3: SoundManager（Godot Node 实现）

**Files:**
- Create: `Src/frame/audio/SoundManager.cs`

> 本 Task 无 Godot 场景树单测；完成后依赖 Task 4 手测。实现须满足 spec §4.1。

- [ ] **Step 1: 实现完整 `SoundManager`**

将下列代码写入 `Src/frame/audio/SoundManager.cs`（可按 Playback / VolumeMute / Cache 加 `#region`）：

```csharp
using System;
using System.Collections.Generic;
using Godot;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.Audio;

public partial class SoundManager : Node, ISoundService
{
	public const int SfxPoolSize = 8;
	public const int MaxCacheSize = 50;
	public const float MuteFadeSeconds = 0.5f;

	private AudioStreamPlayer _bgm = null!;
	private AudioStreamPlayer _ambient = null!;
	private readonly List<AudioStreamPlayer> _sfxPool = new();
	private readonly Dictionary<string, AudioStream> _cache = new();
	private readonly LinkedList<string> _cacheOrder = new();
	private readonly int[] _volumePercent = [100, 100, 100];
	private int _muteFlag;
	private Tween? _muteTween;

	public SoundManager()
	{
		// 在进树前创建子播放器，避免 AddChild 后立刻 Apply Settings 时 _Ready 尚未执行
		_bgm = CreatePlayer("BgmPlayer", "Sound");
		_ambient = CreatePlayer("AmbientPlayer", "Sound");
		for (var i = 0; i < SfxPoolSize; i++)
		{
			_sfxPool.Add(CreatePlayer($"SfxPlayer_{i}", "SFX"));
		}
	}

	#region Playback

	public void PlayBgm(string resourcePath) => PlayOn(_bgm, resourcePath);

	public void StopBgm() => _bgm.Stop();

	public void PlayAmbient(string resourcePath) => PlayOn(_ambient, resourcePath);

	public void StopAmbient() => _ambient.Stop();

	public void PlaySfx(string resourcePath)
	{
		if (string.IsNullOrWhiteSpace(resourcePath))
		{
			AppLog.Warning("PlaySfx 路径为空", "Sound");
			return;
		}

		var stream = TryLoadStream(resourcePath);
		if (stream == null)
		{
			return;
		}

		var player = FindSfxPlayer();
		player.Stream = stream;
		player.Play();
	}

	private void PlayOn(AudioStreamPlayer player, string resourcePath)
	{
		if (string.IsNullOrWhiteSpace(resourcePath))
		{
			AppLog.Warning("音频路径为空", "Sound");
			return;
		}

		var stream = TryLoadStream(resourcePath);
		if (stream == null)
		{
			return;
		}

		player.Stream = stream;
		player.Play();
	}

	private AudioStreamPlayer FindSfxPlayer()
	{
		AudioStreamPlayer? idle = null;
		AudioStreamPlayer? oldest = null;
		var oldestPos = -1.0;

		foreach (var p in _sfxPool)
		{
			if (!p.Playing)
			{
				idle = p;
				break;
			}

			var pos = p.GetPlaybackPosition();
			if (pos >= oldestPos)
			{
				oldestPos = pos;
				oldest = p;
			}
		}

		return idle ?? oldest ?? _sfxPool[0];
	}

	#endregion

	#region VolumeMute

	public void SetBusVolumePercent(int busIndex, int volumePercent)
	{
		if (busIndex < 0 || busIndex >= SoundBus.Count)
		{
			AppLog.Warning($"非法总线索引: {busIndex}", "Sound");
			return;
		}

		var clamped = Math.Clamp(volumePercent, 0, 100);
		_volumePercent[busIndex] = clamped;
		if (!IsMuted(busIndex))
		{
			AudioServer.SetBusVolumeLinear(busIndex, clamped / 100f);
		}
	}

	public int GetBusVolumePercent(int busIndex)
	{
		if (busIndex < 0 || busIndex >= SoundBus.Count)
		{
			AppLog.Warning($"非法总线索引: {busIndex}", "Sound");
			return 100;
		}

		return _volumePercent[busIndex];
	}

	public void SetMuteFlag(int muteFlag)
	{
		var flip = _muteFlag ^ muteFlag;
		_muteFlag = muteFlag;

		ApplyMuteFlagsImmediate();

		if (flip == 0)
		{
			return;
		}

		_muteTween?.Kill();
		_muteTween = CreateTween();
		_muteTween.SetParallel(true);

		for (var bus = 0; bus < SoundBus.Count; bus++)
		{
			if ((flip & (1 << bus)) == 0)
			{
				continue;
			}

			var busIndex = bus;
			var targetLinear = IsMuted(busIndex) ? 0f : _volumePercent[busIndex] / 100f;
			var fromLinear = IsMuted(busIndex)
				? _volumePercent[busIndex] / 100f
				: 0f;

			// 取消静音：先 unmute（ApplyMuteFlagsImmediate 已做），从 0 淡入到目标
			// 进入静音：先保持可听音量再淡出到 0（mute 位已设，但淡出期间用线性音量表现）
			if (IsMuted(busIndex))
			{
				AudioServer.SetBusMute(busIndex, false);
			}

			_muteTween.TweenMethod(
				Callable.From((float v) => AudioServer.SetBusVolumeLinear(busIndex, v)),
				fromLinear,
				targetLinear,
				MuteFadeSeconds);

			if (IsMuted(busIndex))
			{
				var captured = busIndex;
				_muteTween.TweenCallback(Callable.From(() =>
				{
					AudioServer.SetBusMute(captured, true);
					AudioServer.SetBusVolumeLinear(captured, _volumePercent[captured] / 100f);
				})).SetDelay(MuteFadeSeconds);
			}
		}
	}

	public int GetMuteFlag() => _muteFlag;

	private void ApplyMuteFlagsImmediate()
	{
		for (var bus = 0; bus < SoundBus.Count; bus++)
		{
			var muted = IsMuted(bus);
			if (bus == SoundBus.Sound)
			{
				_bgm.StreamPaused = muted;
				_ambient.StreamPaused = muted;
			}

			// 淡入淡出过程中 Mute 由 Tween 回调最终落地；此处对未参与 flip 的总线保持一致
			if (_muteTween == null || !_muteTween.IsRunning())
			{
				AudioServer.SetBusMute(bus, muted);
				if (!muted)
				{
					AudioServer.SetBusVolumeLinear(bus, _volumePercent[bus] / 100f);
				}
			}
		}
	}

	private bool IsMuted(int busIndex) => (_muteFlag & (1 << busIndex)) != 0;

	#endregion

	#region Cache

	public void ClearCache()
	{
		_cache.Clear();
		_cacheOrder.Clear();
	}

	private AudioStreamPlayer CreatePlayer(string name, string bus)
	{
		var player = new AudioStreamPlayer { Name = name, Bus = bus };
		AddChild(player);
		return player;
	}

	private AudioStream? TryLoadStream(string resourcePath)
	{
		if (_cache.TryGetValue(resourcePath, out var cached))
		{
			_cacheOrder.Remove(resourcePath);
			_cacheOrder.AddLast(resourcePath);
			return cached;
		}

		if (!ResourceLoader.Exists(resourcePath))
		{
			AppLog.Warning($"音频资源不存在: {resourcePath}", "Sound");
			return null;
		}

		var stream = ResourceLoader.Load<AudioStream>(resourcePath);
		if (stream == null)
		{
			AppLog.Warning($"音频资源加载失败: {resourcePath}", "Sound");
			return null;
		}

		if (_cache.Count >= MaxCacheSize && _cacheOrder.First != null)
		{
			var oldest = _cacheOrder.First.Value;
			_cacheOrder.RemoveFirst();
			_cache.Remove(oldest);
		}

		_cache[resourcePath] = stream;
		_cacheOrder.AddLast(resourcePath);
		return stream;
	}

	#endregion
}
```

说明：`SetMuteFlag` 中对正在淡出的总线会短暂 `SetBusMute(false)` 以便用音量淡出；结束后再 mute 并恢复线性音量到用户设定（听感仍静音）。若实现时发现并行 Tween 与 `SetDelay` 行为不符合预期，可改为顺序 Tween 或单一 `TweenMethod` 按 `flip` 位批量插值，但语义须保持「静音淡出 / 取消淡入」。

- [ ] **Step 2: 编译主项目确认无错误**

```powershell
dotnet build kemo_card.csproj --no-restore
```

若需 restore：`dotnet build kemo_card.csproj`。Expected: 成功（0 Error）。

- [ ] **Step 3: 格式化并提交**

```powershell
dotnet format kemo_card.csproj --include Src/frame/audio/SoundManager.cs
git add Src/frame/audio/SoundManager.cs
git commit -m "feat(audio): 实现 SoundManager 节点（BGM/Ambient/SFX/静音）"
```

---

### Task 4: MainRoot 接入 + 纳入 bus layout

**Files:**
- Modify: `Src/MainRoot.cs`
- Add (if untracked): `default_bus_layout.tres`

- [ ] **Step 1: 修改 MainRoot**

在 `using` 中增加：

```csharp
using KemoCard.Frame.Audio;
```

在 `_Ready` 中，于 `AppLog.Configure` 之后、`BootstrapServices` 之后插入音频初始化（Bootstrap 会 `LoadFromDisk`，之后 Settings 才可用）：

```csharp
public override void _Ready()
{
	var appLog = new GodotAppLog();
	AppLog.Configure(appLog);

	BootstrapServices();
	InitSoundManager();
	InitUIManager();
	EnsureKeywordTipLayer();
	_ = GlobalModController.OpenMenuAsync();

	EventDispatcher.Configure(new GodotEventDispatcherLogger(appLog));
}

private void InitSoundManager()
{
	var manager = new SoundManager();
	AddChild(manager);
	Sound.Configure(manager);

	var settings = AppRoot.Services.GlobalController.Snapshot.Settings;
	AudioSettingsLoader.Apply(Sound /* 或 manager */, settings);
}
```

注意：`AudioSettingsLoader.Apply` 第一个参数类型为 `ISoundService`，传 `manager` 或通过门面均可；推荐传 `manager` 避免门面未配置窗口期（此处已 Configure，传 `Sound` 静态转发也可）。推荐：

```csharp
AudioSettingsLoader.Apply(manager, settings);
```

在 `_ExitTree` 中，于 `AppRoot.Shutdown()` **之前**：

```csharp
Sound.Configure(NullSoundService.Instance);
```

完整 `_ExitTree`：

```csharp
public override void _ExitTree()
{
	Sound.Configure(NullSoundService.Instance);
	GlobalEvents.Bus.OffAll();
	AppRoot.Shutdown();
	base._ExitTree();
}
```

- [ ] **Step 2: 确认 `default_bus_layout.tres` 在仓库根且含 Sound/SFX**

文件内容应含 `bus/1/name = &"Sound"` 与 `bus/2/name = &"SFX"`。Godot 默认加载 `res://default_bus_layout.tres`，无需改 `project.godot`。

- [ ] **Step 3: 编译 + 跑相关单测**

```powershell
dotnet build kemo_card.csproj
dotnet test Tests/kemo_card.Ui.Tests/kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~SoundFacadeTests|FullyQualifiedName~AudioSettingsLoaderTests" -v n
```

Expected: 构建成功，测试 PASS。

- [ ] **Step 4: 格式化并提交**

```powershell
dotnet format kemo_card.csproj --include Src/MainRoot.cs
git add Src/MainRoot.cs default_bus_layout.tres
git commit -m "feat(audio): MainRoot 挂载 SoundManager 并恢复存档音量"
```

---

### Task 5: 规格状态 + 手测清单

**Files:**
- Modify: `Doc/superpowers/specs/2026-07-14-sound-manager-design.md`（状态行改为「已实现」或「已实现待手测」）

- [ ] **Step 1: 编辑器手测清单（逐项勾选）**

1. 启动游戏无报错；`AudioServer` 可见 Master/Sound/SFX。  
2. 临时在某处（如 `MenuWin._Ready` 调试代码，测完删除）调用 `Sound.PlayBgm("res://...")` / `PlayAmbient` / `PlaySfx` 验证双路叠播与 SFX。  
3. `Sound.SetBusVolumePercent(SoundBus.Sfx, 0)` 后 SFX 听不见；恢复 100 可听见。  
4. `Sound.SetMuteFlag(1 << SoundBus.Sound)` 淡出后 BGM/Ambient 静音；清 0 后淡入恢复。  
5. 错误路径：`Sound.PlaySfx("res://no_such.ogg")` 出现 `AppLog` Warning，不崩。  
6. 退出场景/停止运行后无异常；再次运行门面正常。

- [ ] **Step 2: 更新 spec 状态并提交**

将文首状态改为：`**状态**：已实现`。

```powershell
git add Doc/superpowers/specs/2026-07-14-sound-manager-design.md
git commit -m "docs: 将音频管理器设计规格标记为已实现"
```

---

## Spec 覆盖自检

| Spec 条目 | Task |
|-----------|------|
| ISoundService / Null / Sound 门面 | Task 1 |
| SoundBus 常量 | Task 1 |
| AudioSettingKeys + 启动恢复逻辑 | Task 2 + 4 |
| SoundManager BGM/Ambient/SFX/LRU/静音渐变 | Task 3 |
| MainRoot Configure / Exit 重置 | Task 4 |
| default_bus_layout | Task 4 |
| 单测门面与 Settings | Task 1–2 |
| 非目标（设置 UI 等）未纳入 | — |

---

## 执行说明

实现时严格按 Task 顺序；每 Task 提交一次。`SoundManager` 内播放/静音细节以 spec §4.1 为准；若与本计划注释冲突，以 spec 为准。
