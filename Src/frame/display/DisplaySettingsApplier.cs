using Godot;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.Display;

public static class DisplaySettingsApplier
{
    public static void Apply(Window window, DisplaySettingsState state)
    {
        ArgumentNullException.ThrowIfNull(window);
        ApplyWindowMode(window, state.WindowMode);
        ApplyResolution(window, state.ResolutionId);
        DisplayServer.WindowSetVsyncMode(
            state.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = state.MaxFps;
    }

    public static void ApplyWindowMode(Window window, string modeId)
    {
        window.Borderless = modeId is WindowModeIds.BorderlessWindow or WindowModeIds.BorderlessFullscreen;
        window.Mode = modeId switch
        {
            WindowModeIds.Fullscreen => Window.ModeEnum.ExclusiveFullscreen,
            WindowModeIds.BorderlessFullscreen => Window.ModeEnum.Fullscreen,
            WindowModeIds.BorderlessWindow => Window.ModeEnum.Windowed,
            _ => Window.ModeEnum.Windowed,
        };
    }

    public static void ApplyResolution(Window window, string resolutionId)
    {
        if (!ResolutionRegistry.TryGet(resolutionId, out var entry))
        {
            AppLog.Warning($"未知分辨率 {resolutionId}，回退默认", "DisplaySettings");
            if (!ResolutionRegistry.TryGet(DisplaySettingKeys.DefaultResolutionId, out entry))
                return;
        }
        window.Size = new Vector2I(entry.Width, entry.Height);
    }
}