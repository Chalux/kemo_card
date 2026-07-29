namespace KemoCard.Mod.Combat;

/// <summary>
/// 手牌槽位效果占位；后续里程碑替换为 BuffInstance 并接入结算管线。
/// </summary>
public sealed record HandSlotEffectRef(string BuffId, IReadOnlyDictionary<string, object>? Params);