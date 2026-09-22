using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Content.Keywords;

namespace KemoCard.Mod.Global.Glossary;

/// <summary>词典分组（标题 + 条目）。</summary>
public sealed record GlossarySection(string TitleKey, IReadOnlyList<GlossaryEntry> Entries);

/// <summary>
/// 词典条目：标题与正文都是**翻译键**（正文可带 <see cref="Args"/> 形式参数，
/// 界面用 <c>string.Format(Tr(BodyKey), Args)</c> 渲染）。
/// </summary>
/// <remarks>
/// 只带键不带成品文案：一是内容与语言解耦（切语言不用重建模型），二是模型可以脱离引擎单测
/// （<c>Localization.Tr</c> 依赖 Godot 运行时）。键查不到时界面回落显示键本身，
/// 因此 <see cref="TitleKey"/> 允许直接塞内容 id（如充能球类型没有显示名时）。
/// </remarks>
public sealed record GlossaryEntry(
    string Id,
    string TitleKey,
    string BodyKey,
    IReadOnlyList<object>? Args = null);

/// <summary>
/// 词典内容装配：把"机制词条"（<see cref="KeywordCatalog"/>）与"内容侧玩法元素"（充能球类型）
/// 拼成分组结构。纯函数，不依赖 Godot，可直接单测。
/// </summary>
public static class GlossaryBuilder
{
    /// <summary>机制词条分组标题键。</summary>
    public const string KeywordsSectionKey = "UI_GLOSSARY_SECTION_KEYWORDS";

    /// <summary>充能球分组标题键。</summary>
    public const string OrbsSectionKey = "UI_GLOSSARY_SECTION_ORBS";

    /// <summary>充能球正文键（按伤害类型/是否纯效果球取一条）。</summary>
    public const string OrbBodyElementalKey = "UI_GLOSSARY_ORB_BODY_ELEMENTAL";
    public const string OrbBodyPhysicalKey = "UI_GLOSSARY_ORB_BODY_PHYSICAL";
    public const string OrbBodyMagicalKey = "UI_GLOSSARY_ORB_BODY_MAGICAL";
    public const string OrbBodySupportKey = "UI_GLOSSARY_ORB_BODY_SUPPORT";

    /// <summary>
    /// 装配词典全部分组：机制词条（注册顺序）→ 充能球（内容 id 字典序）。
    /// </summary>
    /// <param name="store">内容注册表（取充能球类型）。</param>
    /// <param name="catalog">词条表；缺省用全局共享实例。</param>
    public static IReadOnlyList<GlossarySection> Build(GameDefinitionStore store, KeywordCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var keywords = catalog ?? KeywordCatalog.Shared;

        var sections = new List<GlossarySection>(2)
        {
            new(KeywordsSectionKey, BuildKeywordEntries(keywords)),
        };

        var orbs = BuildOrbEntries(store);
        if (orbs.Count > 0)
        {
            sections.Add(new GlossarySection(OrbsSectionKey, orbs));
        }

        return sections;
    }

    private static IReadOnlyList<GlossaryEntry> BuildKeywordEntries(KeywordCatalog catalog)
    {
        var entries = new List<GlossaryEntry>();
        foreach (var keyword in catalog.Entries)
        {
            entries.Add(new GlossaryEntry(keyword.Id, keyword.TitleKey, keyword.DescKey));
        }

        return entries;
    }

    private static IReadOnlyList<GlossaryEntry> BuildOrbEntries(GameDefinitionStore store)
    {
        var entries = new List<GlossaryEntry>();
        foreach (var orb in store.OrbTypes.Values.OrderBy(orb => orb.Id, StringComparer.Ordinal))
        {
            entries.Add(new GlossaryEntry(
                orb.Id,
                string.IsNullOrWhiteSpace(orb.DisplayNameId) ? orb.Id : orb.DisplayNameId,
                ResolveOrbBodyKey(orb),
                ResolveOrbBodyArgs(orb)));
        }

        return entries;
    }

    /// <summary>纯效果球 → 支援球文案；否则按伤害类型分物理 / 魔法 / 元素。</summary>
    private static string ResolveOrbBodyKey(OrbTypeDto orb)
    {
        if (!orb.DealsDamage)
        {
            return OrbBodySupportKey;
        }

        return orb.DamageKind switch
        {
            EDamageKind.Physical => OrbBodyPhysicalKey,
            EDamageKind.Magical => OrbBodyMagicalKey,
            _ => OrbBodyElementalKey,
        };
    }

    /// <summary>伤害球正文参数：每球固定值 + 攻击加成百分比；纯效果球无参数。</summary>
    private static IReadOnlyList<object>? ResolveOrbBodyArgs(OrbTypeDto orb) =>
        orb.DealsDamage
            ? [orb.PerOrbAmount, MathF.Round(orb.AttackBonusScale * 100f)]
            : null;
}
