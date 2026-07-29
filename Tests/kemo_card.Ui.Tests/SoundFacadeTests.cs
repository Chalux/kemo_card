using KemoCard.Frame.Audio;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class SoundFacadeTests
{
    [TearDown]
    public void TearDown()
    {
        Sound.Configure(NullSoundService.Instance);
    }

    [Test]
    public void Configure_forwards_calls_to_implementation()
    {
        var recording = new RecordingSoundService();
        Sound.Configure(recording);

        Sound.PlayBgm("res://a.ogg");
        Sound.StopBgm();
        Sound.PlayAmbient("res://b.ogg");
        Sound.StopAmbient();
        Sound.PlaySfx("res://c.ogg");
        Sound.SetBusVolumePercent(SoundBus.Master, 80);
        _ = Sound.GetBusVolumePercent(SoundBus.Sound);
        Sound.SetMuteFlag(1);
        _ = Sound.GetMuteFlag();
        Sound.ClearCache();

        Assert.That(recording.Calls, Is.EqualTo(new[]
        {
            "PlayBgm:res://a.ogg",
            "StopBgm",
            "PlayAmbient:res://b.ogg",
            "StopAmbient",
            "PlaySfx:res://c.ogg",
            "SetBusVolumePercent:0:80",
            "GetBusVolumePercent:1",
            "SetMuteFlag:1",
            "GetMuteFlag",
            "ClearCache",
        }));
    }

    [Test]
    public void Without_configure_does_not_throw()
    {
        Sound.Configure(NullSoundService.Instance);
        Assert.DoesNotThrow(() =>
        {
            Sound.PlayBgm("res://x.ogg");
            Sound.PlayAmbient("res://x.ogg");
            Sound.PlaySfx("res://x.ogg");
            Sound.SetBusVolumePercent(0, 50);
            _ = Sound.GetBusVolumePercent(0);
            Sound.SetMuteFlag(0);
            _ = Sound.GetMuteFlag();
            Sound.ClearCache();
        });
    }

    [Test]
    public void Configure_null_throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => Sound.Configure(null!));
    }
}