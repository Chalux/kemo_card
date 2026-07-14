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
