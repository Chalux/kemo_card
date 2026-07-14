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

        Assert.That(recording.Calls, Is.EqualTo(new[]
        {
            $"SetBusVolumePercent:{SoundBus.Master}:100",
            $"SetBusVolumePercent:{SoundBus.Sound}:100",
            $"SetBusVolumePercent:{SoundBus.Sfx}:999",
            "SetMuteFlag:0",
        }));
    }
}
