namespace KemoCard.Mod.Run;

/// <summary>Run 阶段的判定助手：避免各处重复写 <c>Phase is Battle or BattleEnd</c> 这类条件而漂移。</summary>
public static class ERunPhaseExtensions
{
    /// <summary>
    /// 是否处于战斗阶段。战斗中**不可落盘**：战斗态（模拟器、手牌、充能球队列）不在 Run 存档模型里，
    /// 中途存下来的档读回来只能得到一个半截状态，因此保存类入口在战斗阶段一律禁用。
    /// </summary>
    public static bool IsCombatPhase(this ERunPhase phase) => phase is ERunPhase.Battle or ERunPhase.BattleEnd;
}
