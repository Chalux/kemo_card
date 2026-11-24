using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Global.Ui.Tip;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 左栏的非操控角色卡片：名字、攻防、回复量、护盾、常驻技能按钮与多档充能；点击切换操控。
/// 悬停显示该角色的主动技（每一档 + 当前可释放档高亮，见 <see cref="CombatActiveSkillTips"/>）。
/// 已标记卡牌条不挂在本组件内（Godot GUI 拾取按树序，挂左栏会被战场单位截走悬停），
/// </summary>
public partial class PartyMemberCmp : BaseCmp
{
    [Export] private Label? _lblName;
    [Export] private Label? _lblAttack;
    [Export] private Label? _lblMagicAttack;
    [Export] private Label? _lblDefense;
    [Export] private Label? _lblMagicDefense;
    [Export] private Label? _lblHeal;
    [Export] private Label? _lblShield;
    [Export] private Label? _lblCharges;
    [Export] private Label? _lblState;
    [Export] private Label? _lblMark;
    [Export] private TextureRect? _crosshair;
    [Export] private Button? _btnRelease;

    /// <summary>点击回调，参数为该卡片绑定的槽位索引。</summary>
    public Action<int>? Clicked { get; set; }

    /// <summary>「释放主动技」回调，参数为该卡片绑定的槽位索引。</summary>
    public Action<int>? ReleaseRequested { get; set; }

    public int SlotIndex { get; private set; } = -1;

    private bool _interactable;
    private CharacterBattleInstance? _character;

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
        if (_btnRelease != null)
        {
            // 按钮自身消费点击（BaseButton），不会冒泡到卡片的切换操控。
            OnClicks(_btnRelease, () =>
            {
                if (SlotIndex >= 0)
                    ReleaseRequested?.Invoke(SlotIndex);
            });
        }

        Binder.OnMouseEnterExit(this, OnHoverEntered, OnHoverExited);
    }

    protected override void OnExitTree() => KeywordTipService.Current?.HideTips(this);

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
        _character = character;

        if (_lblName != null)
            _lblName.Text = CombatUnitFormat.DisplayName(definition?.DisplayNameId, character.DefinitionId);
        if (_lblAttack != null)
            _lblAttack.Text = CombatUnitFormat.PhysicalAttack(character.Asc);
        if (_lblMagicAttack != null)
            _lblMagicAttack.Text = CombatUnitFormat.MagicAttack(character.Asc);
        if (_lblDefense != null)
            _lblDefense.Text = CombatUnitFormat.PhysicalDefense(character.Asc);
        if (_lblMagicDefense != null)
            _lblMagicDefense.Text = CombatUnitFormat.MagicDefense(character.Asc);
        if (_lblHeal != null)
            _lblHeal.Text = CombatUnitFormat.Heal(character.Asc);
        if (_lblShield != null)
            _lblShield.Text = CombatUnitFormat.Shield(character.Asc);
        if (_lblCharges != null)
        {
            _lblCharges.Text = CombatActiveSkillTips.ChargeDots(character);
            _lblCharges.Visible = _lblCharges.Text.Length > 0;
        }

        if (_lblState != null)
        {
            var stateKey = !canControl ? "UI_COMBAT_NO_CONTROL" : character.IsSealed ? "UI_COMBAT_SEALED" : "";
            _lblState.Visible = stateKey.Length > 0;
            _lblState.Text = stateKey.Length > 0 ? Localization.Tr(stateKey) : "";
        }

        CombatActionMarks.Apply(_lblMark, mark);

        Modulate = Modulate with { A = _interactable ? 1f : 0.6f };
        MouseDefaultCursorShape = _interactable ? CursorShape.PointingHand : CursorShape.Arrow;
    }

    /// <summary>嘲讽标识：是否显示 Crosshair（判定见 <see cref="CombatTauntMarks"/>）。</summary>
    public void SetTauntMark(bool marked)
    {
        if (_crosshair != null)
            _crosshair.Visible = marked;
    }

    /// <summary>按钮常驻；未准备好、无权控制或结算中灰显。</summary>
    public void SetSkillRelease(bool enabled)
    {
        if (_btnRelease != null)
        {
            _btnRelease.Visible = true;
            _btnRelease.Disabled = !enabled;
        }
    }

    private void OnHoverEntered()
    {
        if (_character is not null)
            CombatActiveSkillTips.TryShow(this, _character, TipSide.Right);
    }

    private void OnHoverExited() => KeywordTipService.Current?.HideTips(this);
}