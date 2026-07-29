namespace KemoCard.Mod.Combat.Effects;

/// <summary>规格 §4.6：中途弃牌通道，决定可否弃已标记牌。</summary>
public enum EDiscardChannel
{
    /// <summary>主动技弃牌：可弃已标记，命中则取消标记、退费、回退未确认。</summary>
    ActiveSkill,

    /// <summary>卡牌执行阶段弃牌：仅从未标记手牌均匀随机。</summary>
    CardExecution,

    /// <summary>敌方行动 / Buff 钩子 / 回合结束等：同 CardExecution。</summary>
    Other,
}