namespace KemoCard.Mod.Combat.Orbs;

/// <summary>
/// 队列中的一个充能球：类型 + <b>产球者</b>（产球时的玩家槽位索引；&lt;0 = 无产球者）。
/// 伤害与触发效果都以产球者为源——见 <see cref="OrbRuntime"/>。
/// </summary>
public readonly record struct OrbInstance(string OrbTypeId, int ProducerIndex);
