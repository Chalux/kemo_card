using KemoCard.Frame.Content.Keywords;
using KemoCard.Frame.Logging;

namespace KemoCard.Mod.Global.Def;

/// <summary>
/// 本体内置词条注册。
/// </summary>
public static class BuiltinKeywords
{
    public static void RegisterAll(KeywordCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        catalog.WarningHandler ??= msg => AppLog.Warning(msg, "Keyword");

        catalog.Register(new KeywordEntry("exhaust", "KW_EXHAUST_TITLE", "KW_EXHAUST_DESC"));
        catalog.Register(new KeywordEntry("retain", "KW_RETAIN_TITLE", "KW_RETAIN_DESC"));
        catalog.Register(new KeywordEntry("deal_damage", "KW_DEAL_DAMAGE_TITLE", "KW_DEAL_DAMAGE_DESC"));
        catalog.Register(new KeywordEntry("hand_slot_damage", "KW_HAND_SLOT_DAMAGE_TITLE", "KW_HAND_SLOT_DAMAGE_DESC"));
        catalog.Register(new KeywordEntry("charge", "KW_CHARGE_TITLE", "KW_CHARGE_DESC"));
        catalog.Register(new KeywordEntry("chain", "KW_CHAIN_TITLE", "KW_CHAIN_DESC"));
    }
}