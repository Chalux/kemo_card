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
