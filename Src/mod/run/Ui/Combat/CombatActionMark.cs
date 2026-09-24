using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Mod.Combat.NormalAttack;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Global.Ui.Themes;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>本回合普攻参与标识（Run 规格 §14.2）：队友卡 / 操控角色卡共用一套文案与配色。</summary>
public enum ECombatActionMark
{
    None,

    /// <summary>本回合普攻归属者（<c>(回合-1) % 队伍人数</c>）。</summary>
    NormalAttack,

    /// <summary>持有追打（<c>trait.follow_up</c>）的非归属者，普攻结算时补打。</summary>
    FollowUp,
}

/// <summary>
/// 普攻标识的判定与落笔：判定是纯函数（可单测），落笔负责把文案 / 颜色写进组件标签。
/// </summary>
public static class CombatActionMarks
{
    /// <summary>
    /// 某槽位本回合的标识：归属者恒为「普攻」（归属者持有追打也不重复出手，故不给追打标识），
    /// 其余持有追打者显示「追打」，都没有则 <see cref="ECombatActionMark.None"/>。
    /// </summary>
    public static ECombatActionMark Resolve(CombatSimulation simulation, int slotIndex)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        var characters = simulation.PlayerTeam.Characters;
        if (slotIndex < 0 || slotIndex >= characters.Count)
            return ECombatActionMark.None;

        if (slotIndex == simulation.NormalAttacks.ResolveSlotIndex(simulation))
            return ECombatActionMark.NormalAttack;

        return FollowUpAttack.ResolvePercent(characters[slotIndex]) > 0f
            ? ECombatActionMark.FollowUp
            : ECombatActionMark.None;
    }

    /// <summary>把标识写进标签（None 隐藏）；两处组件（队友卡 / 操控角色卡）共用，避免文案漂移。</summary>
    public static void Apply(Label? label, ECombatActionMark mark)
    {
        if (label is null)
            return;

        label.Visible = mark != ECombatActionMark.None;
        if (!label.Visible)
            return;

        label.Text = mark switch
        {
            ECombatActionMark.NormalAttack => Localization.Tr("UI_COMBAT_MARK_NORMAL_ATTACK"),
            ECombatActionMark.FollowUp => Localization.Tr("UI_COMBAT_MARK_FOLLOW_UP"),
            _ => "",
        };
        label.SelfModulate = mark == ECombatActionMark.NormalAttack ? KemoPalette.Accent : KemoPalette.Danger;
    }
}