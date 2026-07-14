using KemoCard.Frame.Audio;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class MuteFlagBitsTests
{
    [Test]
    public void Set_and_IsMuted_round_trip()
    {
        var flag = 0;
        flag = MuteFlagBits.WithMuted(flag, SoundBus.Master, true);
        flag = MuteFlagBits.WithMuted(flag, SoundBus.Sfx, true);
        Assert.That(MuteFlagBits.IsMuted(flag, SoundBus.Master), Is.True);
        Assert.That(MuteFlagBits.IsMuted(flag, SoundBus.Sound), Is.False);
        Assert.That(MuteFlagBits.IsMuted(flag, SoundBus.Sfx), Is.True);
        Assert.That(flag, Is.EqualTo((1 << SoundBus.Master) | (1 << SoundBus.Sfx)));
    }

    [Test]
    public void WithMuted_false_clears_bit()
    {
        var flag = MuteFlagBits.WithMuted(0, SoundBus.Sound, true);
        flag = MuteFlagBits.WithMuted(flag, SoundBus.Sound, false);
        Assert.That(flag, Is.EqualTo(0));
    }
}
