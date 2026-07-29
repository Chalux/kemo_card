namespace KemoCard.Frame.Content.Keywords;

public sealed class KeywordTipRequest
{
    public KeywordTipRequest(string keywordId, IReadOnlyDictionary<string, string>? parameters = null)
    {
        KeywordId = keywordId ?? "";
        Parameters = parameters ?? new Dictionary<string, string>();
    }

    public string KeywordId { get; }

    public IReadOnlyDictionary<string, string> Parameters { get; }
}