using System;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.Content;

public sealed class GodotContentModLogger : IContentModLogger
{
    private readonly IAppLog _log;

    public GodotContentModLogger(IAppLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void LogSkipped(ModSkipEntry entry)
    {
        _log.Warning($"Skipped {entry.ModId}: {entry.Reason} {entry.Detail}".Trim(), "ContentMod");
    }

    public void LogConflict(ContentIdConflictEntry entry)
    {
        _log.Warning(
            $"Id conflict {entry.Category}/{entry.ContentId}: kept {entry.WinnerModId}, skipped {entry.LoserModId}",
            "ContentMod");
    }

    public void LogValidationError(ContentDefinitionValidationError entry)
    {
        _log.Warning(
            $"Validation {entry.Category}/{entry.DefinitionId}: {entry.Message}",
            "ContentMod");
    }

    public void LogScriptLoadError(ScriptLoadError entry)
    {
        _log.Warning($"Script load {entry.ModId}/{entry.ScriptPath}: {entry.Message}", "ContentMod");
    }
}