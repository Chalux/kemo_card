namespace KemoCard.Mod.Combat;

/// <summary>
/// 主动技蓄力链在战斗内的一档快照（规格 §5.1）。
/// </summary>
/// <param name="SkillId">该档释放的技能 id。</param>
/// <param name="Cooldown">该档冷却 <c>C_k</c>；累计阈值 <c>T_k = C_0 + … + C_k</c>。</param>
public sealed record ActiveSkillTier(string SkillId, int Cooldown);
