using KemoCard.Frame.Logging;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class AppLogTests
{
    [TearDown]
    public void TearDown()
    {
        AppLog.Configure(NullAppLog.Instance);
    }

    [Test]
    public void Configure_forwards_calls_to_implementation()
    {
        var recording = new RecordingAppLog();
        AppLog.Configure(recording);

        AppLog.Debug("d", "Cat");
        AppLog.Info("i");
        AppLog.Warning("w", "UI");
        AppLog.Error("e", "MainRoot");

        Assert.That(recording.Entries, Has.Count.EqualTo(4));
        Assert.That(recording.Entries[0], Is.EqualTo(new AppLogEntry(AppLogLevel.Debug, "d", "Cat")));
        Assert.That(recording.Entries[1], Is.EqualTo(new AppLogEntry(AppLogLevel.Info, "i", null)));
        Assert.That(recording.Entries[2], Is.EqualTo(new AppLogEntry(AppLogLevel.Warning, "w", "UI")));
        Assert.That(recording.Entries[3], Is.EqualTo(new AppLogEntry(AppLogLevel.Error, "e", "MainRoot")));
    }

    [Test]
    public void Without_configure_does_not_throw()
    {
        AppLog.Configure(NullAppLog.Instance);
        Assert.DoesNotThrow(() =>
        {
            AppLog.Debug("x");
            AppLog.Info("x");
            AppLog.Warning("x");
            AppLog.Error("x");
        });
    }

    [Test]
    public void Configure_null_throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => AppLog.Configure(null!));
    }
}