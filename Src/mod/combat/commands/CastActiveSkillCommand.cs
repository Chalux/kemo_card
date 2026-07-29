using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Commands;

/// <summary>
/// 释放主动技（规格 §5.4）。**不带 skillId**——由宿主按当前技能计数器 <c>S</c> 解析出将释放的档位。
/// 即时结算、不入卡牌队列、不扣可用能量、不占已行动。
/// </summary>
public sealed record CastActiveSkillCommand(
	int CharacterIndex,
	IReadOnlyList<CombatTargetRef> Targets) : ICombatCommand;
