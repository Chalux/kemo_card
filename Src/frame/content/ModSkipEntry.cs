namespace KemoCard.Frame.Content;

public sealed record ModSkipEntry(string ModId, ModSkipReason Reason, string? Detail = null);