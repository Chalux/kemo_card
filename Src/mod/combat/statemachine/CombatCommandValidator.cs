using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.StateMachine;

/// <summary>指令进入费用与队列写入前的状态、内容及目标校验。</summary>
internal static class CombatCommandValidator
{
    public static CombatApplyResult ValidateCardMark(CombatSimulation simulation, PlayCardCommand command,
        out CharacterBattleInstance character, out HandSlot slot, out CardDto card,
        out IReadOnlyList<CombatTargetRef> targets, out int paid)
    {
        character = null!;
        slot = null!;
        card = null!;
        targets = [];
        paid = 0;
        if (!TryGetCharacter(simulation, command.CharacterIndex, out character, out var error))
            return new CombatApplyResult(false, error);
        if (character.IsSealed)
            return new CombatApplyResult(false, "角色处于封印，无法标记卡牌。");
        if (character.HasActed)
            return new CombatApplyResult(false, "角色已确认，须先取消确认才能标记卡牌。");
        if (command.HandSlotIndex < 0 || command.HandSlotIndex >= character.HandSlots.Count)
            return new CombatApplyResult(false, "手牌槽位无效。");

        slot = character.HandSlots[command.HandSlotIndex];
        if (slot.IsEmpty || slot.CardId is null || slot.RuntimeInstanceId is null)
            return new CombatApplyResult(false, "指定槽位没有卡牌。");
        if (slot.IsMarked)
            return new CombatApplyResult(false, "该卡牌已标记入队。");
        if (!simulation.Definitions.Store.TryGetCard(slot.CardId, out card))
            return new CombatApplyResult(false, "卡牌定义不存在。");
        if (card.CostType is not (ECostType.None or ECostType.Energy))
            return new CombatApplyResult(false, "该费用类型尚未实装，无法标记入队。");

        paid = CardCostCalculator.Compute(simulation, command.CharacterIndex, card, slot.RuntimeInstanceId);
        if (character.AvailableEnergy < paid)
            return new CombatApplyResult(false, "可用能量不足。");

        targets = CombatTargeting.ResolvePlayerTargets(simulation, card.TargetSide, card.TargetScope,
            card.TargetCount, command.CharacterIndex, command.Targets, out var targetError)!;
        if (targets is null)
            return new CombatApplyResult(false, targetError);

        return new CombatApplyResult(true);
    }

    public static bool TryGetCharacter(
        CombatSimulation simulation,
        int characterIndex,
        out CharacterBattleInstance character,
        out string error)
    {
        var characters = simulation.PlayerTeam.Characters;
        if (characterIndex < 0 || characterIndex >= characters.Count)
        {
            character = null!;
            error = "角色索引无效。";
            return false;
        }

        character = characters[characterIndex];
        error = string.Empty;
        return true;
    }

}