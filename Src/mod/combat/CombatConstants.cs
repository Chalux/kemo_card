namespace KemoCard.Mod.Combat;

public static class CombatConstants
{
    public const int MaxDecksPerCharacter = 10;
    public const int MinCardsPerDeck = 1;
    public const int MaxCardsPerDeck = 10;
    public const int HandSlotCount = 5;

    /// <summary>队伍槽位数量：玩家阶段「全员已行动」判定与开战校验共用，避免多处硬编码 4。</summary>
    public const int SlotCount = 4;

    /// <summary>规格 §2.5：封印以 GAS GrantedTags 约定标签承载。</summary>
    public const string SealedTag = "combat.state.sealed";
}