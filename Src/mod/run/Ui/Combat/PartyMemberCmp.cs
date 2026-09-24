using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 左栏的非操控角色卡片：名字、物攻·魔攻、物防·魔防、回复量；点击切换操控（仅有权控制的槽位）。
/// </summary>
public partial class PartyMemberCmp : BaseCmp
{
    [Export] private Label? _lblName;
    [Export] private Label? _lblAttack;
    [Export] private Label? _lblDefense;
    [Export] private Label? _lblHeal;
    [Export] private Label? _lblState;
    [Export] private Label? _lblMark;

    /// <summary>点击回调，参数为该卡片绑定的槽位索引。</summary>
    public Action<int>? Clicked { get; set; }

    public int SlotIndex { get; private set; } = -1;

    private bool _interactable;

    protected override void OnReady()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    protected override void InitEvent()
    {
        OnClicks(this, () =>
        {
            if (_interactable && SlotIndex >= 0)
                Clicked?.Invoke(SlotIndex);
        });
    }

    public void Bind(
        int slotIndex,
        CharacterBattleInstance character,
        CharacterDto? definition,
        bool canControl,
        bool interactable,
        ECombatActionMark mark)
    {
        SlotIndex = slotIndex;
        _interactable = interactable && canControl;

        if (_lblName != null)
            _lblName.Text = CombatUnitFormat.DisplayName(definition?.DisplayNameId, character.DefinitionId);
        if (_lblAttack != null)
            _lblAttack.Text = CombatUnitFormat.Attack(character.Asc);
        if (_lblDefense != null)
            _lblDefense.Text = CombatUnitFormat.Defense(character.Asc);
        if (_lblHeal != null)
            _lblHeal.Text = CombatUnitFormat.Heal(character.Asc);

        if (_lblState != null)
        {
            var stateKey = !canControl
                ? "UI_COMBAT_NO_CONTROL"
                : character.IsSealed
                    ? "UI_COMBAT_SEALED"
                    : character.HasActed
                        ? "UI_COMBAT_CONFIRMED"
                        : "";
            _lblState.Visible = stateKey.Length > 0;
            _lblState.Text = stateKey.Length > 0 ? Localization.Tr(stateKey) : "";
        }

        CombatActionMarks.Apply(_lblMark, mark);

        Modulate = Modulate with { A = _interactable ? 1f : 0.6f };
        MouseDefaultCursorShape = _interactable ? CursorShape.PointingHand : CursorShape.Arrow;
    }
}