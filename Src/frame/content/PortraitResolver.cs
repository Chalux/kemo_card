using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Content;

public static class PortraitResolver
{
    public static CharacterPortraitsDto NormalizePortraits(CharacterDto character)
    {
        ArgumentNullException.ThrowIfNull(character);

        var neutralKey = nameof(EPortraitKey.Neutral);

        if (character.Portraits is null)
        {
            if (string.IsNullOrEmpty(character.ArtPath))
                return new CharacterPortraitsDto();

            return new CharacterPortraitsDto
            {
                Default = neutralKey,
                Entries =
                [
                    new PortraitEntryDto { Key = neutralKey, Path = character.ArtPath },
                ],
            };
        }

        if (string.IsNullOrEmpty(character.ArtPath))
            return character.Portraits;

        var hasNeutral = character.Portraits.Entries.Any(entry =>
            string.Equals(entry.Key, neutralKey, StringComparison.Ordinal));
        if (hasNeutral)
            return character.Portraits;

        var entries = new List<PortraitEntryDto>(character.Portraits.Entries)
        {
            new() { Key = neutralKey, Path = character.ArtPath },
        };

        return new CharacterPortraitsDto
        {
            Default = character.Portraits.Default,
            Entries = entries,
            Routes = character.Portraits.Routes,
        };
    }

    public static string ResolveByRoute(
        CharacterDto character,
        string routeKey,
        Func<string, bool> pathExists)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(routeKey);
        ArgumentNullException.ThrowIfNull(pathExists);

        var portraits = NormalizePortraits(character);
        if (!portraits.Routes.TryGetValue(routeKey, out var targetKey))
            return ResolveByKey(character, nameof(EPortraitKey.Neutral), pathExists);

        return ResolveByKey(character, targetKey, pathExists);
    }

    public static string ResolveByKey(
        CharacterDto character,
        string portraitKey,
        Func<string, bool> pathExists)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(portraitKey);
        ArgumentNullException.ThrowIfNull(pathExists);

        var portraits = NormalizePortraits(character);
        var neutralKey = nameof(EPortraitKey.Neutral);

        var path = FindPath(portraits, portraitKey);
        if (path is not null && pathExists(path))
            return path;

        var neutralPath = FindPath(portraits, neutralKey);
        if (neutralPath is not null && pathExists(neutralPath))
            return neutralPath;

        return "";
    }

    private static string? FindPath(CharacterPortraitsDto portraits, string key)
    {
        foreach (var entry in portraits.Entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.Ordinal))
                return entry.Path;
        }

        return null;
    }
}
