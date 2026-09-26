using System.Text;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Global.Ui;

public readonly record struct CharacterSummaryTip(string Title, string Body);

public static class CharacterSummaryBuilder
{
    public static CharacterSummaryTip Build(
        CharacterDto character,
        Func<string, SkillDto?> resolveSkill,
        Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(resolveSkill);
        ArgumentNullException.ThrowIfNull(translate);

        var title = string.IsNullOrWhiteSpace(character.DisplayNameId)
            ? ""
            : translate(character.DisplayNameId);

        var lines = new List<string>();

        var meta = BuildMetaLine(character, translate);
        if (!string.IsNullOrEmpty(meta))
        {
            lines.Add(meta);
        }

        var skills = CardSummaryBuilder.StripRichText(BuildSkillText(character, resolveSkill, translate));
        if (!string.IsNullOrWhiteSpace(skills))
        {
            lines.Add(skills.Trim());
        }

        return new CharacterSummaryTip(title, string.Join("\n", lines));
    }

    /// <summary>
    /// 身份三元组（元素 / 定位 / 种族）的显示名口径与角色详情一致
    /// （<see cref="CharacterIdentityLabels"/>）；摘要行只列值、不列字段名。
    /// </summary>
    private static string BuildMetaLine(CharacterDto character, Func<string, string> translate)
    {
        var parts = new List<string>();

        var elements = CharacterIdentityLabels.Element(character.Element, translate);
        if (!string.IsNullOrEmpty(elements))
        {
            parts.Add(elements);
        }

        if (character.Role != ERole.None)
        {
            parts.Add(CharacterIdentityLabels.Role(character.Role, translate));
        }

        var races = CharacterIdentityLabels.Race(character.Race, translate);
        if (!string.IsNullOrEmpty(races))
        {
            parts.Add(races);
        }

        return string.Join(" ", parts);
    }

    private static string BuildSkillText(
        CharacterDto character,
        Func<string, SkillDto?> resolveSkill,
        Func<string, string> translate)
    {
        var sb = new StringBuilder();

        // 主动技（2026-09-26）：名称 + 蓄力门槛 + 描述；与角色详情 / 卡组编辑同一口径。
        foreach (var entry in ActiveSkillTextBuilder.Entries(character, resolveSkill, translate))
        {
            AppendLine(sb, CardSummaryBuilder.StripRichText(entry));
        }

        // 扁平 skillRefs：角色旧字段（当前内容未使用），保留原有"只列描述"的行为。
        foreach (var skillRef in character.SkillRefs)
        {
            if (string.IsNullOrWhiteSpace(skillRef.SkillId))
            {
                continue;
            }

            var skill = resolveSkill(skillRef.SkillId);
            if (skill == null || string.IsNullOrEmpty(skill.DescId))
            {
                continue;
            }

            var part = translate(skill.DescId);
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            AppendLine(sb, part);
        }

        return sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (sb.Length > 0)
        {
            sb.Append('\n');
        }

        sb.Append(text.Trim());
    }
}