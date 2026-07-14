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
