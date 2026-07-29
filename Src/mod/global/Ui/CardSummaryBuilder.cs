using System.Text.RegularExpressions;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Def;

namespace KemoCard.Mod.Global.Ui;

public readonly record struct CardSummaryTip(string Title, string Body);

public static partial class CardSummaryBuilder
{
    [GeneratedRegex(@"\[url=[^\]]*\](.*?)\[/url\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UrlTagRegex();

    [GeneratedRegex(@"\[/?[^\]]+\]")]
    private static partial Regex OtherTagRegex();

    public static CardSummaryTip Build(
        CardDto card,
        Func<string, SkillDto?> resolveSkill,
        Func<string, string> translate,
        Func<string, string?> resolveExclusiveCharacterName)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(resolveSkill);
        ArgumentNullException.ThrowIfNull(translate);
        ArgumentNullException.ThrowIfNull(resolveExclusiveCharacterName);

        var title = string.IsNullOrWhiteSpace(card.DisplayNameId)
            ? ""
            : translate(card.DisplayNameId);

        var lines = new List<string>();

        var meta = BuildMetaLine(card, translate);
        if (!string.IsNullOrEmpty(meta))
        {
            lines.Add(meta);
        }

        var effect = StripRichText(CardDescBuilder.Build(card, resolveSkill, translate));
        if (!string.IsNullOrWhiteSpace(effect))
        {
            lines.Add(effect.Trim());
        }

        if (card.IsExclusive)
        {
            var exclusiveName = resolveExclusiveCharacterName(card.Id);
            if (!string.IsNullOrWhiteSpace(exclusiveName))
            {
                lines.Add(translate(CardUiDefinitions.ExclusivePrefixKey) + exclusiveName);
            }
        }

        return new CardSummaryTip(title, string.Join("\n", lines));
    }

    public static string StripRichText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        try
        {
            var withoutUrl = UrlTagRegex().Replace(text, "$1");
            return OtherTagRegex().Replace(withoutUrl, "");
        }
        catch
        {
            return text;
        }
    }

    private static string BuildMetaLine(CardDto card, Func<string, string> translate)
    {
        var parts = new List<string>();

        var cost = CardUiDefinitions.FormatCostForTip(card.CostType, card.Cost, translate);
        if (!string.IsNullOrEmpty(cost))
        {
            parts.Add(cost);
        }

        var elements = FormatElements(card.Element, translate);
        if (!string.IsNullOrEmpty(elements))
        {
            parts.Add(elements);
        }

        if (card.Role != ERole.None
            && CodexFilterDefinitions.TryGetRoleLocaleKey(card.Role, out var roleKey))
        {
            parts.Add(translate(roleKey));
        }

        return string.Join(" ", parts);
    }

    private static string FormatElements(int elementFlags, Func<string, string> translate)
    {
        if (elementFlags == 0)
        {
            return "";
        }

        var names = new List<string>();
        foreach (EElement e in Enum.GetValues<EElement>())
        {
            if (e == EElement.None)
            {
                continue;
            }

            if ((elementFlags & (int)e) == 0)
            {
                continue;
            }

            if (CodexFilterDefinitions.TryGetElementLocaleKey(e, out var key))
            {
                names.Add(translate(key));
            }
        }

        return names.Count == 0 ? "" : string.Join("、", names);
    }
}