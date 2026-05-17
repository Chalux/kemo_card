namespace KemoCard.Frame.Content;

public enum ModSkipReason
{
	InvalidManifest,
	DuplicateModId,
	MissingRequiredDependency,
	CyclicDependency,
	LoadFailed,
}
