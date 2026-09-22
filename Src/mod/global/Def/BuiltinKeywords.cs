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
        // 玩法机制词条：这些概念只有规则、没有卡面图标，词典是玩家唯一的查阅入口。
        catalog.Register(new KeywordEntry("normal_attack", "KW_NORMAL_ATTACK_TITLE", "KW_NORMAL_ATTACK_DESC"));
        catalog.Register(new KeywordEntry("orb", "KW_ORB_TITLE", "KW_ORB_DESC"));
        catalog.Register(new KeywordEntry("orb_element", "KW_ORB_ELEMENT_TITLE", "KW_ORB_ELEMENT_DESC"));
        catalog.Register(new KeywordEntry("shared_hp", "KW_SHARED_HP_TITLE", "KW_SHARED_HP_DESC"));
        catalog.Register(new KeywordEntry("team_potential", "KW_TEAM_POTENTIAL_TITLE", "KW_TEAM_POTENTIAL_DESC"));
        catalog.Register(new KeywordEntry("character_passive", "KW_CHARACTER_PASSIVE_TITLE", "KW_CHARACTER_PASSIVE_DESC"));
        catalog.Register(new KeywordEntry("follow_up", "KW_FOLLOW_UP_TITLE", "KW_FOLLOW_UP_DESC"));
        catalog.Register(new KeywordEntry("seal", "KW_SEAL_TITLE", "KW_SEAL_DESC"));
    }
}