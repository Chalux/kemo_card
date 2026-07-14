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

    private readonly AudioStreamPlayer _bgm;
    private readonly AudioStreamPlayer _ambient;
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

        ApplyStreamPaused();

        if (flip == 0)
        {
            ApplyMuteFlagsImmediate();
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

            // 取消静音：先 unmute，从 0 淡入到目标
            // 进入静音：短暂 unmute 以便用音量淡出，结束后再 mute
            AudioServer.SetBusMute(busIndex, false);

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

        // 未 flip 的总线立即对齐 mute/音量
        for (var bus = 0; bus < SoundBus.Count; bus++)
        {
            if ((flip & (1 << bus)) != 0)
            {
                continue;
            }

            var muted = IsMuted(bus);
            AudioServer.SetBusMute(bus, muted);
            if (!muted)
            {
                AudioServer.SetBusVolumeLinear(bus, _volumePercent[bus] / 100f);
            }
        }
    }

    public int GetMuteFlag() => _muteFlag;

    private void ApplyMuteFlagsImmediate()
    {
        for (var bus = 0; bus < SoundBus.Count; bus++)
        {
            var muted = IsMuted(bus);
            AudioServer.SetBusMute(bus, muted);
            if (!muted)
            {
                AudioServer.SetBusVolumeLinear(bus, _volumePercent[bus] / 100f);
            }
        }
    }

    private void ApplyStreamPaused()
    {
        var soundMuted = IsMuted(SoundBus.Sound);
        _bgm.StreamPaused = soundMuted;
        _ambient.StreamPaused = soundMuted;
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
