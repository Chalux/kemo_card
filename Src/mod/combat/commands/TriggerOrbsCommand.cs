namespace KemoCard.Mod.Combat.Commands;

/// <summary>
/// 主动触发充能球（出牌阶段；球数不足门槛时失败且不动队列）。
/// 充能球是全队共享资源，不绑定单个角色，因此 <see cref="CharacterIndex"/> 恒为 -1。
/// </summary>
public sealed record TriggerOrbsCommand : ICombatCommand
{
    public int CharacterIndex => -1;
}
