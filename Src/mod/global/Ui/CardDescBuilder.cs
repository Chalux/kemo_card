using System.Text;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Global.Ui;

public static class CardDescBuilder
{
    private const string KeywordMetaPrefix = "kw:";

    public static string Build(
        CardDto card,
        Func<string, SkillDto?> resolveSkill,
        Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(resolveSkill);
        ArgumentNullException.ThrowIfNull(translate);

        var sb = new StringBuilder();
        foreach (var skillRef in card.SkillRefs)
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

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(part);
        }

        return sb.ToString();
    }

    public static bool TryParseKeywordMeta(string? meta, out string keywordId)
    {
        keywordId = "";
        if (string.IsNullOrWhiteSpace(meta) || !meta.StartsWith(KeywordMetaPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var id = meta[KeywordMetaPrefix.Length..].Trim();
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        keywordId = id;
        return true;
    }
}