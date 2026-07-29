namespace KemoCard.Mod.Combat;

public static class CombatConstants
{
    public const int MaxDecksPerCharacter = 10;
    public const int MinCardsPerDeck = 1;
    public const int MaxCardsPerDeck = 10;
    public const int HandSlotCount = 5;

    /// <summary>规格 §2.5：封印以 GAS GrantedTags 约定标签承载。</summary>
    public const string SealedTag = "combat.state.sealed";
}