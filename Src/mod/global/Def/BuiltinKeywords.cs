using Godot;
using KemoCard.Frame.Content.Keywords;

namespace KemoCard.Mod.Global.Def;

/// <summary>
/// 本体内置词条注册。
/// </summary>
public static class BuiltinKeywords
{
    public static void RegisterAll(KeywordCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        catalog.WarningHandler ??= msg => GD.PushWarning(msg);

        catalog.Register(new KeywordEntry("exhaust", "KW_EXHAUST_TITLE", "KW_EXHAUST_DESC"));
        catalog.Register(new KeywordEntry("retain", "KW_RETAIN_TITLE", "KW_RETAIN_DESC"));
        catalog.Register(new KeywordEntry("deal_damage", "KW_DEAL_DAMAGE_TITLE", "KW_DEAL_DAMAGE_DESC"));
    }
}
