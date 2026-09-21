namespace KemoCard.Mod.Combat.Runtime;

/// <summary>
/// 开战注入的被动 buff 条目：Run 层按潜能解锁状态构造（槽序 + 潜能档低→高），
/// 在 BattleStart 管线的技能注入之后、冻结补满 SharedHp 之前挂载。
/// </summary>
public sealed record BattleStartBuffEntry(
    int CharacterIndex,
    string BuffId,
    IReadOnlyDictionary<string, object>? Params = null);
