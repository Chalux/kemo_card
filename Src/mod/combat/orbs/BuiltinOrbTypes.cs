using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat.Orbs;

/// <summary>
/// 内建充能球类型 id（内容侧由 base-game 的 <c>content/orbs/*.json</c> 声明）。
/// 回合结束统计<b>只</b>产出这 6 种；Mod 注册的特殊球必须由角色/卡牌/效果授予。
/// </summary>
public static class BuiltinOrbTypes
{
    public const string Red = "red";
    public const string Blue = "blue";
    public const string Green = "green";
    public const string Yellow = "yellow";
    public const string Physical = "physical";
    public const string Magic = "magic";

    /// <summary>四属性球（回合结束按本回合出牌的元素分布产 1 个）。</summary>
    public static readonly IReadOnlyList<(EElement Element, string OrbTypeId)> ElementOrbs =
    [
        (EElement.Red, Red),
        (EElement.Blue, Blue),
        (EElement.Green, Green),
        (EElement.Yellow, Yellow),
    ];

    /// <summary>物理 / 魔法球（回合结束按本回合出牌的卡牌类型分布产 1 个）。</summary>
    public static readonly IReadOnlyList<string> AttackOrbs = [Physical, Magic];
}
