namespace KemoCard.Mod.Combat;

public sealed class DeckValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> InvalidCardIds { get; init; } = [];

    public static DeckValidationResult Ok() => new() { IsValid = true };

    public static DeckValidationResult Fail(IReadOnlyList<string> invalidCardIds) => new()
    {
        IsValid = false,
        InvalidCardIds = invalidCardIds,
    };
}