namespace KemoCard.Frame.Display;

public static class WindowModeIds
{
    public const string Windowed = "windowed";
    public const string BorderlessWindow = "borderless_window";
    public const string BorderlessFullscreen = "borderless_fullscreen";
    public const string Fullscreen = "fullscreen";

    public static bool IsKnown(string modeId) =>
        modeId is Windowed or BorderlessWindow or BorderlessFullscreen or Fullscreen;
}
