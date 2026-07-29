namespace KemoCard.Frame.Audio;

public static class MuteFlagBits
{
    public static bool IsMuted(int muteFlag, int busIndex) =>
        (muteFlag & (1 << busIndex)) != 0;

    public static int WithMuted(int muteFlag, int busIndex, bool muted) =>
        muted ? muteFlag | (1 << busIndex) : muteFlag & ~(1 << busIndex);
}