# 音频管理器（Sound）设计

**日期**：2026-07-14  
**状态**：已实现  
**范围**：frame 层音频服务（BGM / Ambient / SFX、总线音量与静音、流缓存、静态门面），以及启动时从 `GlobalSave.Settings` 恢复；不含设置 UI、交叉淡入淡出、空间音频。

**参考**：旧项目 `kemo-card/Scripts/Core/SoundManager.cs`（审查后改进静音/渐变/无 SFX API/硬单例等问题）。

---

## 1. 背景与决策摘要

本项目尚无音频运行时；总线已在 `default_bus_layout.tres` 配置为 **Master / Sound / SFX**（Sound、SFX send 到 Master）。

选定方案：**`ISoundService` + `SoundManager`（Node，位于 `Src/frame/audio/`）+ 静态门面 `Sound.Configure`**，对齐 `AppLog` 的可配置门面模式；`MainRoot` 挂树并 Configure，从全局存档 Settings 恢复音量/静音。

依赖约定（已写入 `.cursor/rules/project-foundations.mdc`）：`frame` **可以**引用 Godot；`frame` **不得**依赖 `mod`；通用 Godot 运行时能力优先放 `frame`，`fixed` 仅放引擎补丁。

---

## 2. 目标与非目标

### 2.1 目标

- Sound 总线双路叠播：**BGM** + **Ambient**（各自独立 Play/Stop）。
- SFX：固定容量对象池（默认 8）播放；调用方传完整 `res://...` 路径。
- Master / Sound / SFX 音量（0–100%）与位掩码静音；静音渐变不叠 Tween、方向正确。
- `AudioStream` LRU 缓存（上限 50）。
- 静态门面 `Sound.*` 供散落调用；未配置时 `NullSoundService` 空操作。
- 音量/静音键写入 `GlobalSave.Settings`；启动时读取并应用（设置 UI 本轮不做，写回由日后设置页或调用方负责）。

### 2.2 非目标

- 设置界面。
- BGM 交叉淡入淡出、多 Ambient 槽、3D/空间音频。
- 按 mod 包声明音频清单或短名解析（路径由调用方给完整 `res://`）。
- Godot 场景树集成测试（本轮以门面/空实现单测 + 编辑器手测为主）。

---

## 3. 架构

```
调用方（UI / 战斗 / MainRoot …）
        │
        └─ Sound.PlayBgm / PlayAmbient / PlaySfx / SetVolume / SetMute …
                    │
                    ▼
              ISoundService  ←── NullSoundService（未配置 / 单测）
                    │
                    ▼
         SoundManager : Node（Src/frame/audio）
           ├─ AudioStreamPlayer  ×2（Bus=Sound：BGM / Ambient）
           ├─ AudioStreamPlayer  池（Bus=SFX）
           ├─ AudioStream LRU 缓存
           └─ AudioServer（Master / Sound / SFX 音量与静音）
```

| 职责 | 路径 |
|------|------|
| `ISoundService`、`NullSoundService`、静态 `Sound`、`SoundBus`、`SoundManager` | `Src/frame/audio/` |
| 挂树 + `Sound.Configure` + 从存档恢复 | `MainRoot`（经 `GlobalModController` 读 Settings） |
| Settings 键读写约定 | 见 §5；**不**让 `frame` 依赖存档 / `mod` |

退出：`MainRoot._ExitTree` 将门面重置为 `NullSoundService`，再执行现有 `AppRoot.Shutdown()` 等清理。

---

## 4. API 形状

```csharp
namespace KemoCard.Frame.Audio;

public static class SoundBus
{
    public const int Master = 0;
    public const int Sound = 1;  // BGM + Ambient
    public const int Sfx = 2;
    public const int Count = 3;
}

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

public static class Sound
{
    public static void Configure(ISoundService implementation);
    // 转发 ISoundService 全部方法；默认 NullSoundService
}
```

### 4.1 行为约定

| 主题 | 约定 |
|------|------|
| 路径 | 完整 `res://...`；空路径 / 加载失败 → `AppLog.Warning`，忽略本次播放 |
| 同路径再次 Play | 从头播放 |
| 静音与 Play | **不**因静音而跳过 `Play*`；靠总线 mute；Sound 轨对 BGM/Ambient 另设 `StreamPaused` |
| 静音渐变 | 约 0.5s；先 Kill 旧 Tween；静音 = 淡出再 mute；取消 = unmute 再淡入 |
| 音量 | `AudioServer` 总线线性音量；`volumePercent` clamp 到 0–100；非法 `busIndex` → Warning 并 return |
| SFX 池满 | 抢最早开始的那路（保证反馈不丢） |
| 缓存 | LRU，上限 50；`ClearCache` 清空 |

相对旧实现的主要改进：补齐 SFX；静音不吞掉 Play；渐变方向与 Tween 不叠加；无硬单例，可测门面。

---

## 5. Settings 键与启动恢复

存于 `GlobalSave.Settings`（`Dictionary<string, string>`）：

| 键 | 含义 | 默认 |
|----|------|------|
| `audio.master_volume` | Master 音量 0–100 | `"100"` |
| `audio.sound_volume` | Sound 总线音量 | `"100"` |
| `audio.sfx_volume` | SFX 总线音量 | `"100"` |
| `audio.mute_flag` | 位掩码：bit0 Master, bit1 Sound, bit2 Sfx | `"0"` |

`MainRoot` 在 Bootstrap / 读全局档后解析上述键并调用 `Sound.SetBusVolumePercent` / `SetMuteFlag`。本轮不实现设置 UI；日后设置页在改音量/静音时写回 `GlobalModController.SetSetting`（或等价 API）。

---

## 6. 生命周期摘要

1. **_Ready**：创建 `SoundManager` 子节点 → `Sound.Configure` → 读 Settings 应用音量/静音。  
2. **运行**：业务 `Sound.Play*` / 音量静音 API。  
3. **_ExitTree**：`Sound.Configure(NullSoundService.Instance)` → 现有关闭流程。

---

## 7. 测试

- 单测：`NullSoundService` 空操作；`Sound.Configure` 后门面转发到假实现（记录调用）。  
- 编辑器手测：双路叠播、SFX 池、音量/静音渐变、错误路径 Warning。

---

## 8. 常量

| 常量 | 值 |
|------|-----|
| SFX 池容量 | 8 |
| LRU 缓存上限 | 50 |
| 静音渐变时长 | 0.5s |
