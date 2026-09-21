using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Rules;

public sealed class DamagePacket
{
    public CombatTargetRef Source { get; init; }
    public CombatTargetRef Target { get; init; }
    public float Amount { get; set; }
    public string? EffectId { get; init; }

    /// <summary>
    /// 伤害类型维度：物理 / 魔法 / 元素（2026-09-20 与元素维度拆分，此前是一个混用的字符串标签）。
    /// 物理与魔法吃目标对应防御；<see cref="EDamageKind.Elemental"/> 不吃防御。
    /// </summary>
    public EDamageKind Kind { get; init; } = EDamageKind.Physical;

    /// <summary>
    /// 元素维度（<c>[Flags]</c>，可多元素或 <see cref="EElement.None"/>）。
    /// 普通攻击带出攻击者的全部元素；充能球元素球带该球的元素，物理/魔法球为 None。
    /// 当前尚无元素克制/抗性的消费方，供未来规则与统计使用。
    /// </summary>
    public EElement Element { get; init; } = EElement.None;
}
