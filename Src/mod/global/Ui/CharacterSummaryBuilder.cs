using System.Text;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Def;

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

    private static string BuildMetaLine(CharacterDto character, Func<string, string> translate)
    {
        var parts = new List<string>();

        var elements = FormatElements(character.Element, translate);
        if (!string.IsNullOrEmpty(elements))
        {
            parts.Add(elements);
        }

        if (character.Role != ERole.None
            && CodexFilterDefinitions.TryGetRoleLocaleKey(character.Role, out var roleKey))
        {
            parts.Add(translate(roleKey));
        }

        var races = FormatRaces(character.Race, translate);
        if (!string.IsNullOrEmpty(races))
        {
            parts.Add(races);
        }

        return string.Join(" ", parts);
    }

    private static string FormatElements(EElement elementFlags, Func<string, string> translate)
    {
        if (elementFlags == EElement.None)
        {
            return "";
        }

        var names = new List<string>();
        foreach (EElement element in Enum.GetValues<EElement>())
        {
            if (element == EElement.None)
            {
                continue;
            }

            if ((elementFlags & element) == 0)
            {
                continue;
            }

            if (CodexFilterDefinitions.TryGetElementLocaleKey(element, out var key))
            {
                names.Add(translate(key));
            }
        }

        return names.Count == 0 ? "" : string.Join("、", names);
    }

    private static string FormatRaces(ERace raceFlags, Func<string, string> translate)
    {
        if (raceFlags == ERace.None)
        {
            return "";
        }

        var names = new List<string>();
        foreach (ERace race in Enum.GetValues<ERace>())
        {
            if (race == ERace.None)
            {
                continue;
            }

            if ((raceFlags & race) == 0)
            {
                continue;
            }

            if (CodexFilterDefinitions.TryGetRaceLocaleKey(race, out var key))
            {
                names.Add(translate(key));
            }
        }

        return names.Count == 0 ? "" : string.Join("、", names);
    }

    private static string BuildSkillText(
        CharacterDto character,
        Func<string, SkillDto?> resolveSkill,
        Func<string, string> translate)
    {
        var sb = new StringBuilder();
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

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(part);
        }

        return sb.ToString();
    }
}
