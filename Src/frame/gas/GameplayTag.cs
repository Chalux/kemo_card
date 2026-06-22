namespace KemoCard.Frame.Gas;

public static class GameplayTag
{
	public static bool Matches(string ownedTag, string queryTag)
	{
		if (string.IsNullOrEmpty(ownedTag) || string.IsNullOrEmpty(queryTag))
			return false;

		if (string.Equals(ownedTag, queryTag, StringComparison.Ordinal))
			return true;

		return ownedTag.StartsWith(queryTag + ".", StringComparison.Ordinal);
	}
}
