using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Def;

namespace KemoCard.Mod.Global.Ui;

/// <summary>
/// 角色身份显示名（元素 / 定位 / 种族）的唯一拼接口径：角色详情、队伍编辑预览与角色悬停摘要共用。
/// </summary>
/// <remarks>
/// <para>元素与种族是 Flags，多标志按 <c>、</c> 连接；三者一律走
/// <see cref="CodexFilterDefinitions"/> 的本地化键（与图鉴筛选同一套 <c>UI_ELEMENT_*</c> /
/// <c>UI_ROLE_*</c> / <c>UI_RACE_*</c>），缺键时才回落枚举原名——直接插枚举值会在界面露出
/// <c>SwordMan</c> / <c>Animal, Dragon</c> 这类英文枚举名。</para>
/// <para>取 <paramref name="translate"/> 委托而不是直接调 <c>Localization.Tr</c>：拼接逻辑要能脱离
/// Godot 单测（与 <see cref="CharacterSummaryBuilder"/> 同约定）。</para>
/// </remarks>
public static class CharacterIdentityLabels
{
    private const string ElementLabelKey = "UI_CHARACTER_META_ELEMENT";
    private const string RoleLabelKey = "UI_CHARACTER_META_ROLE";
    private const string RaceLabelKey = "UI_CHARACTER_META_RACE";
    private const string FieldFormatKey = "UI_CHARACTER_META_FIELD";

    /// <summary>字段之间与多标志之间的连接符。</summary>
    private const string FieldSeparator = " · ";
    private const string FlagSeparator = "、";

    /// <summary>元素显示名（如「红、蓝」）；无元素时返回 <c>""</c>。</summary>
    public static string Element(EElement element, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(translate);

        var names = new List<string>();
        foreach (var flag in Enum.GetValues<EElement>())
        {
            if (flag != EElement.None
                && (element & flag) != 0
                && CodexFilterDefinitions.TryGetElementLocaleKey(flag, out var key))
            {
                names.Add(translate(key));
            }
        }

        return string.Join(FlagSeparator, names);
    }

    /// <summary>种族显示名（如「动物、龙族」）；无种族时返回 <c>""</c>。</summary>
    public static string Race(ERace race, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(translate);

        var names = new List<string>();
        foreach (var flag in Enum.GetValues<ERace>())
        {
            if (flag != ERace.None
                && (race & flag) != 0
                && CodexFilterDefinitions.TryGetRaceLocaleKey(flag, out var key))
            {
                names.Add(translate(key));
            }
        }

        return string.Join(FlagSeparator, names);
    }

    /// <summary>
    /// 定位显示名。与元素 / 种族不同，<see cref="ERole"/> 不是 Flags，也**不**在此吞掉
    /// <see cref="ERole.None"/>：队伍编辑预览要显示「无」，而需要省略该字段的调用方
    /// （<see cref="MetaLine"/>、<see cref="CharacterSummaryBuilder"/>）自行判 <c>None</c>。
    /// </summary>
    public static string Role(ERole role, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(translate);

        return CodexFilterDefinitions.TryGetRoleLocaleKey(role, out var key) ? translate(key) : role.ToString();
    }

    /// <summary>
    /// 角色详情的身份行（如「元素：蓝 · 定位：战士 · 种族：动物、龙族」）：
    /// 字段全取自角色定义，**不依赖 Run**，因此图鉴里点开的角色也有值。
    /// 字段为空（无元素 / 无定位 / 无种族）时整段省略；三者皆无时返回 <c>""</c>。
    /// </summary>
    public static string MetaLine(CharacterDto character, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(translate);

        var parts = new List<string>(3);
        AddField(parts, translate, ElementLabelKey, Element(character.Element, translate));
        if (character.Role != ERole.None)
        {
            AddField(parts, translate, RoleLabelKey, Role(character.Role, translate));
        }

        AddField(parts, translate, RaceLabelKey, Race(character.Race, translate));
        return string.Join(FieldSeparator, parts);
    }

    private static void AddField(List<string> parts, Func<string, string> translate, string labelKey, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        parts.Add(string.Format(translate(FieldFormatKey), translate(labelKey), value));
    }
}