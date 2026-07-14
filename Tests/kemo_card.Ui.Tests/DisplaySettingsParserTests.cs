using System.Collections.Generic;
using KemoCard.Frame.Display;
using KemoCard.Frame.Locale;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class DisplaySettingsParserTests
{
    [SetUp]
    public void SetUp()
    {
        ResolutionRegistry.ResetToBuiltinsForTests();
        LocaleRegistry.ResetToBuiltinsForTests();
    }

    [Test]
    public void Parse_uses_defaults_when_missing()
    {
        var state = DisplaySettingsParser.Parse(new Dictionary<string, string>());
        Assert.That(state.WindowMode, Is.EqualTo(WindowModeIds.Windowed));
        Assert.That(state.ResolutionId, Is.EqualTo(DisplaySettingKeys.DefaultResolutionId));
        Assert.That(state.VSync, Is.True);
        Assert.That(state.MaxFps, Is.EqualTo(60));
        Assert.That(state.LanguageCode, Is.EqualTo(LocaleSettingKeys.DefaultLanguage));
    }

    [Test]
    public void Parse_reads_valid_values()
    {
        var state = DisplaySettingsParser.Parse(new Dictionary<string, string>
        {
            [DisplaySettingKeys.WindowMode] = WindowModeIds.Fullscreen,
            [DisplaySettingKeys.Resolution] = "2560x1440",
            [DisplaySettingKeys.VSync] = "0",
            [DisplaySettingKeys.MaxFps] = "144",
            [LocaleSettingKeys.Language] = "en",
        });
        Assert.That(state.WindowMode, Is.EqualTo(WindowModeIds.Fullscreen));
        Assert.That(state.ResolutionId, Is.EqualTo("2560x1440"));
        Assert.That(state.VSync, Is.False);
        Assert.That(state.MaxFps, Is.EqualTo(144));
        Assert.That(state.LanguageCode, Is.EqualTo("en"));
    }

    [Test]
    public void Parse_falls_back_on_invalid()
    {
        var state = DisplaySettingsParser.Parse(new Dictionary<string, string>
        {
            [DisplaySettingKeys.WindowMode] = "nope",
            [DisplaySettingKeys.Resolution] = "1x1",
            [DisplaySettingKeys.VSync] = "x",
            [DisplaySettingKeys.MaxFps] = "-3",
            [LocaleSettingKeys.Language] = "fr",
        });
        Assert.That(state.WindowMode, Is.EqualTo(WindowModeIds.Windowed));
        Assert.That(state.ResolutionId, Is.EqualTo(DisplaySettingKeys.DefaultResolutionId));
        Assert.That(state.VSync, Is.True);
        Assert.That(state.MaxFps, Is.EqualTo(60));
        Assert.That(state.LanguageCode, Is.EqualTo(LocaleSettingKeys.DefaultLanguage));
    }

    [Test]
    public void Parse_allows_unlimited_fps_zero()
    {
        var state = DisplaySettingsParser.Parse(new Dictionary<string, string>
        {
            [DisplaySettingKeys.MaxFps] = "0",
        });
        Assert.That(state.MaxFps, Is.EqualTo(0));
    }
}
