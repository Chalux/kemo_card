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