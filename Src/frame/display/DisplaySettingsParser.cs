using System.Collections.Generic;
using KemoCard.Frame.Locale;

namespace KemoCard.Frame.Display;

public readonly record struct DisplaySettingsState(
    string WindowMode,
    string ResolutionId,
    bool VSync,
    int MaxFps,
    string LanguageCode);

public static class DisplaySettingsParser
{
    public static DisplaySettingsState Parse(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new DisplaySettingsState(
            ReadWindowMode(settings),
            ReadResolution(settings),
            ReadVSync(settings),
            ReadMaxFps(settings),
            ReadLanguage(settings));
    }

    private static string ReadWindowMode(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.TryGetValue(DisplaySettingKeys.WindowMode, out var raw)
            && !string.IsNullOrWhiteSpace(raw)
            && WindowModeIds.IsKnown(raw))
            return raw;
        return WindowModeIds.Windowed;
    }

    private static string ReadResolution(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.TryGetValue(DisplaySettingKeys.Resolution, out var raw)
            && !string.IsNullOrWhiteSpace(raw)
            && ResolutionRegistry.TryGet(raw, out _))
            return raw;
        return DisplaySettingKeys.DefaultResolutionId;
    }

    private static bool ReadVSync(IReadOnlyDictionary<string, string> settings)
    {
        if (!settings.TryGetValue(DisplaySettingKeys.VSync, out var raw) || string.IsNullOrWhiteSpace(raw))
            return DisplaySettingKeys.DefaultVSync;
        if (raw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase))
            return true;
        if (raw.Equals("0", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("false", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("no", StringComparison.OrdinalIgnoreCase))
            return false;
        return DisplaySettingKeys.DefaultVSync;
    }

    private static int ReadMaxFps(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.TryGetValue(DisplaySettingKeys.MaxFps, out var raw)
            && int.TryParse(raw, out var fps)
            && fps >= 0)
            return fps;
        return DisplaySettingKeys.DefaultMaxFps;
    }

    private static string ReadLanguage(IReadOnlyDictionary<string, string> settings)
    {
        if (settings.TryGetValue(LocaleSettingKeys.Language, out var raw)
            && !string.IsNullOrWhiteSpace(raw)
            && LocaleRegistry.TryGet(raw, out _))
            return raw;
        return LocaleSettingKeys.DefaultLanguage;
    }
}