using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Global.Ui;
using KemoCard.Mod.Global.Ui.Themes;
using KemoCard.Mod.Global.Ui.Tip;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 战斗界面主动技的悬停文案与技能进度后缀（2026-09-27）：按运行时蓄力链列出**每一档**
/// （名称 + 累计门槛 + 描述），只有当前可释放的档（<c>ResolveCastableTier</c>，自动最高档）
/// 加【可用】标记并整体高亮；技能进度后缀给出「可用 / 最大」状态。
/// </summary>
/// <remarks>
/// <para><see cref="ResolveProgressSuffix"/> / <see cref="BuildTip"/> 是纯函数（可单测）；
/// <see cref="TryShow"/> 是宿主组件（队友卡 / 操控角色面板）的展示入口，负责查注册表与出提示。</para>
/// <para>档位文案与角色详情 / 卡组编辑共用 <see cref="ActiveSkillTextBuilder"/>（名称 + 门槛 + 描述）；
/// 进度行复用战斗面板的技能计数键 <c>UI_COMBAT_SKILL_COUNTER</c>。</para>
/// </remarks>
public static class CombatActiveSkillTips
{
    /// <summary>可释放档的状态标记（附在该档门槛之后）。</summary>
    public const string ReadyTagKey = "UI_COMBAT_ACTIVE_SKILL_READY";

    /// <summary>技能进度后缀：有可释放档（未满档）。</summary>
    public const string UsableKey = "UI_COMBAT_ACTIVE_SKILL_USABLE";

    /// <summary>技能进度后缀：满档（<c>S</c> 已达 <c>Cap</c>，最高档可释放）。</summary>
    public const string MaxKey = "UI_COMBAT_ACTIVE_SKILL_MAX";

    /// <summary>
    /// 技能进度后缀（已本地化）：满档 → 最大；有可释放档 → 可用；无主动链 / 尚不可用 → <c>null</c>。
    /// 满档优先于可用（单档角色的「可用」即「满档」，只提示最大）。
    /// </summary>
    public static string? ResolveProgressSuffix(CharacterBattleInstance character, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(translate);

        if (character.ActiveSkillChain.Count == 0)
        {
            return null;
        }

        if (character.SkillCounter >= character.SkillCounterCap)
        {
            return translate(MaxKey);
        }

        return character.ResolveCastableTier() >= 0 ? translate(UsableKey) : null;
    }

    /// <summary>
    /// 悬停正文（BBCode）：当前进度（技能 S / Cap）+ 各档；可释放档加【可用】并整体高亮
    /// （<paramref name="highlightColorHex"/> 为 <c>rrggbb</c>，null = 只加标记不上色）。
    /// 无主动技 / 全部档位无法解析时返回 <c>""</c>。
    /// </summary>
    public static string BuildTip(
        CharacterBattleInstance character,
        Func<string, SkillDto?> resolveSkill,
        Func<string, string> translate,
        string? highlightColorHex = null)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(resolveSkill);
        ArgumentNullException.ThrowIfNull(translate);

        var tiers = ActiveSkillTextBuilder.ResolveTiers(
            character.ActiveSkillChain.Select(tier => (tier.SkillId, tier.Cooldown)),
            resolveSkill,
            translate);
        if (tiers.Count == 0)
        {
            return "";
        }

        var castableTier = character.ResolveCastableTier();
        var readyTag = translate(ReadyTagKey);
        var lines = new List<string>(tiers.Count + 1)
        {
            string.Format(translate("UI_COMBAT_SKILL_COUNTER"), character.SkillCounter, character.SkillCounterCap),
        };

        foreach (var tier in tiers)
        {
            var isCastable = tier.TierIndex == castableTier;
            var entry = ActiveSkillTextBuilder.Entry(
                tier.Name,
                tier.Threshold,
                tier.Description,
                translate,
                isCastable ? readyTag : "");
            if (isCastable && !string.IsNullOrWhiteSpace(highlightColorHex))
            {
                entry = $"[color=#{highlightColorHex}]{entry}[/color]";
            }

            lines.Add(entry);
        }

        return string.Join("\n\n", lines);
    }

    /// <summary>
    /// 宿主组件（队友卡 / 操控角色面板）的悬停展示入口：解析技能定义 → 组正文 → 经
    /// <see cref="KeywordTipService"/> 出提示（标题固定「主动技」）。无主动技 / 未初始化时不出提示。
    /// </summary>
    public static bool TryShow(Control anchor, CharacterBattleInstance character, TipSide preferSide)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(character);

        GameDefinitionStore store;
        try
        {
            store = AppRoot.Services.ContentModPipeline.Registry.Store;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var body = BuildTip(
            character,
            id => store.TryGetSkill(id, out var skill) ? skill : null,
            Localization.Tr,
            KemoPalette.TextOnAccent.ToHtml(false));
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        KeywordTipService.Current?.ShowCustomTips(
            anchor,
            [(Localization.Tr(ActiveSkillTextBuilder.TitleKey), body)],
            preferSide);
        return true;
    }
}
