using KemoCard.Fixed.Godot;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Global.Ui;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>战斗界面各组件共用的属性读取与文案拼接口径（角色 / 敌人一致）。</summary>
public static class CombatUnitFormat
{
    private static int Read(AbilitySystemComponent asc, string attributeId) =>
        (int)MathF.Round(asc.GetCurrentValue(attributeId));

    /// <summary>物理攻击值文本。</summary>
    public static string PhysicalAttack(AbilitySystemComponent asc) =>
        Read(asc, AttributeIds.PhysicalAttack).ToString();

    /// <summary>魔法攻击值文本。</summary>
    public static string MagicAttack(AbilitySystemComponent asc) =>
        Read(asc, AttributeIds.MagicAttack).ToString();

    /// <summary>物理防御值文本。</summary>
    public static string PhysicalDefense(AbilitySystemComponent asc) =>
        Read(asc, AttributeIds.PhysicalDefense).ToString();

    /// <summary>魔法防御值文本。</summary>
    public static string MagicDefense(AbilitySystemComponent asc) =>
        Read(asc, AttributeIds.MagicDefense).ToString();

    /// <summary>回复量。</summary>
    public static string Heal(AbilitySystemComponent asc) => Read(asc, AttributeIds.HealPower).ToString();

    /// <summary>「种族·定位」（任一为空则只显示另一项；都为空返回 ""）。</summary>
    public static string RaceAndRole(ERace race, ERole role)
    {
        var raceText = CharacterIdentityLabels.Race(race, Localization.Tr);
        var roleText = role == ERole.None ? "" : CharacterIdentityLabels.Role(role, Localization.Tr);
        return Join(raceText, roleText);
    }

    /// <summary>「元素·定位」。</summary>
    public static string ElementAndRole(EElement element, ERole role)
    {
        var elementText = CharacterIdentityLabels.Element(element, Localization.Tr);
        var roleText = role == ERole.None ? "" : CharacterIdentityLabels.Role(role, Localization.Tr);
        return Join(elementText, roleText);
    }

    public static string DisplayName(string? displayNameId, string fallbackId) =>
        string.IsNullOrWhiteSpace(displayNameId) ? fallbackId : Localization.Tr(displayNameId);

    /// <summary>「无限叠」判定阈值：maxStacks 未配置（≤ 0）或达到该值（内容里 99 = 可无限叠）都显示 ∞。</summary>
    public const int UnlimitedStacks = 99;

    public static bool IsUnlimitedStacks(int maxStacks) => maxStacks <= 0 || maxStacks >= UnlimitedStacks;

    /// <summary>buff 层数文本「当前 / 上限」；无限叠时上限显示 <c>∞</c>（如 <c>3 / ∞</c>）。</summary>
    public static string StacksText(int stacks, int maxStacks) =>
        $"{stacks} / {(IsUnlimitedStacks(maxStacks) ? "∞" : maxStacks.ToString())}";

    private static string Join(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
            return right;
        if (string.IsNullOrEmpty(right))
            return left;
        return $"{left} · {right}";
    }
}