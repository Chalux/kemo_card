using Godot;

namespace KemoCard.Fixed.Godot;

/// <summary>
/// 非 Node 场景下的本地化入口，封装 <see cref="TranslationServer.Translate"/>。
/// </summary>
public static class Localization
{
    public static string Tr(string key) => TranslationServer.Translate(key);
}