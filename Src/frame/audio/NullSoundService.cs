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
