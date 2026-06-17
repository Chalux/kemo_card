namespace KemoCard.Frame.Content;

public sealed record ContentIdConflictEntry(
	EContentCategory Category,
	string ContentId,
	string WinnerModId,
	string LoserModId);
