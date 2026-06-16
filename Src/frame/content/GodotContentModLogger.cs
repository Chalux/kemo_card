using Godot;

namespace KemoCard.Frame.Content;

public sealed class GodotContentModLogger : IContentModLogger
{
	public void LogSkipped(ModSkipEntry entry)
	{
		GD.PushWarning($"[ContentMod] Skipped {entry.ModId}: {entry.Reason} {entry.Detail}".Trim());
	}

	public void LogConflict(ContentIdConflictEntry entry)
	{
		GD.PushWarning(
			$"[ContentMod] Id conflict {entry.Category}/{entry.ContentId}: kept {entry.WinnerModId}, skipped {entry.LoserModId}");
	}

	public void LogValidationError(ContentDefinitionValidationError entry)
	{
		GD.PushWarning(
			$"[ContentMod] Validation {entry.Category}/{entry.DefinitionId}: {entry.Message}");
	}
}
