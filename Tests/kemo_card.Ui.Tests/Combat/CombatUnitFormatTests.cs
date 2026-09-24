using KemoCard.Mod.Run.Ui.CombatUi;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// buff 层数文案口径：无上限（maxStacks ≤ 0）或可无限叠（≥ <see cref="CombatUnitFormat.UnlimitedStacks"/>）
/// 显示 ∞，其余显示实际上限。
/// </summary>
[TestFixture]
public sealed class CombatUnitFormatTests
{
    [TestCase(1, 1, "1 / 1")]
    [TestCase(2, 3, "2 / 3")]
    [TestCase(3, 0, "3 / ∞")]
    [TestCase(3, 99, "3 / ∞")]
    [TestCase(3, 100, "3 / ∞")]
    public void Stacks_text_uses_infinity_for_unlimited_max_stacks(int stacks, int maxStacks, string expected)
    {
        Assert.That(CombatUnitFormat.StacksText(stacks, maxStacks), Is.EqualTo(expected));
    }

    [TestCase(0, true)]
    [TestCase(-1, true)]
    [TestCase(CombatUnitFormat.UnlimitedStacks, true)]
    [TestCase(CombatUnitFormat.UnlimitedStacks + 1, true)]
    [TestCase(1, false)]
    [TestCase(CombatUnitFormat.UnlimitedStacks - 1, false)]
    public void Unlimited_stack_predicate_matches_the_content_convention(int maxStacks, bool expected)
    {
        Assert.That(CombatUnitFormat.IsUnlimitedStacks(maxStacks), Is.EqualTo(expected));
    }
}
