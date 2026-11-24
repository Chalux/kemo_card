namespace KemoCard.Mod.Combat.Runtime;

/// <summary>
/// 开战注入的被动 buff 条目：Run 层按出战槽位和角色定义顺序构造，
/// 在 BattleStart 管线的技能注入之后、冻结补满 SharedHp 之前挂载。
/// </summary>
public sealed record BattleStartBuffEntry(
    int CharacterIndex,
    string BuffId,
    IReadOnlyDictionary<string, object>? Params = null);
