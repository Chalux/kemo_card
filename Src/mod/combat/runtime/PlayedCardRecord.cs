namespace KemoCard.Mod.Combat.Runtime;

/// <summary>
/// 本回合打出的卡牌记录（充能球的回合结束统计口径）：卡牌 id + 打出者槽位。
/// 在卡牌结算入口登记（含空放——牌已离手即算打出），回合结束产出球后清空。
/// </summary>
public readonly record struct PlayedCardRecord(string CardId, int CharacterIndex);
