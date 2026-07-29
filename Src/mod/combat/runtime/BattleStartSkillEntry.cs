namespace KemoCard.Mod.Combat.Runtime;

/// <summary>
/// 一条开战技能注入条目（规格 §6.1 第 1–2 步）。
/// 顺序由调用方（Run 层）负责：被动自动技能按槽序 0→3 且潜能档低→高在前，修饰技能按插入序在后。
/// </summary>
/// <param name="SourceCharacterIndex">施放者槽位索引；<c>-1</c> 表示队伍来源（共享修饰）。</param>
public sealed record BattleStartSkillEntry(string SkillId, int SourceCharacterIndex);