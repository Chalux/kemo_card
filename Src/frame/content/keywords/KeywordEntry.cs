namespace KemoCard.Frame.Content.Keywords;

public sealed class KeywordEntry
{
    public KeywordEntry(string id, string titleKey, string descKey)
    {
        Id = id ?? "";
        TitleKey = titleKey ?? "";
        DescKey = descKey ?? "";
    }

    public string Id { get; }

    public string TitleKey { get; }

    public string DescKey { get; }
}