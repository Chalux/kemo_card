namespace KemoCard.Frame.Gas;

public sealed class GameplayTagContainer
{
    private readonly HashSet<string> _inherentTags = new(StringComparer.Ordinal);
    private readonly HashSet<string> _grantedTags = new(StringComparer.Ordinal);

    public void AddTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        _inherentTags.Add(tag);
    }

    public void RemoveTag(string tag) => _inherentTags.Remove(tag);

    public void AddGrantedTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        _grantedTags.Add(tag);
    }

    public void ClearGrantedTags() => _grantedTags.Clear();

    public bool HasTag(string queryTag)
    {
        if (string.IsNullOrWhiteSpace(queryTag))
            return false;

        foreach (var ownedTag in EnumerateAllTags())
        {
            if (GameplayTag.Matches(ownedTag, queryTag))
                return true;
        }

        return false;
    }

    public bool HasAny(IEnumerable<string> queryTags)
    {
        foreach (var queryTag in queryTags)
        {
            if (HasTag(queryTag))
                return true;
        }

        return false;
    }

    public bool HasAll(IEnumerable<string> queryTags)
    {
        foreach (var queryTag in queryTags)
        {
            if (!HasTag(queryTag))
                return false;
        }

        return true;
    }

    internal bool HasImmunityTo(string categoryTag)
    {
        if (string.IsNullOrWhiteSpace(categoryTag))
            return false;

        if (HasTag(categoryTag))
            return true;

        return HasTag($"immunity.{categoryTag}");
    }

    private IEnumerable<string> EnumerateAllTags()
    {
        foreach (var tag in _inherentTags)
            yield return tag;

        foreach (var tag in _grantedTags)
            yield return tag;
    }
}