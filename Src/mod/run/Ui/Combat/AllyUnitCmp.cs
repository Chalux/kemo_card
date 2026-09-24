using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Global.Ui.Comp;
using KemoCard.Mod.Global.Ui.Themes;
using KemoCard.Mod.Run.Ui.CombatUi.Presentation;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 战场上的友方角色：边框 + <see cref="CharacterPresenter"/>（有 <c>presentation</c> 则播序列帧，否则立绘 / 空白）
/// + 名字 + 当前操控 / 已确认 / 可选目标高亮。位移动画由 <see cref="CombatAnimator"/> 驱动。
/// </summary>
public partial class AllyUnitCmp : BaseCmp
{
    [Export] private CharacterPresenter? _presenter;
    [Export] private Label? _lblName;
    [Export] private Label? _lblState;
    [Export] private Panel? _highlight;

    /// <summary>点击回调（选目标态点友方单位），参数为槽位索引。</summary>
    public Action<int>? Clicked { get; set; }

    public int SlotIndex { get; private set; } = -1;

    private bool _targetable;

    protected override void OnReady()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    protected override void InitEvent()
    {
        OnClicks(this, () =>
        {
            if (_targetable && SlotIndex >= 0)
                Clicked?.Invoke(SlotIndex);
        });
    }

    public void Bind(int slotIndex, CharacterBattleInstance character, CharacterDto? definition)
    {
        SlotIndex = slotIndex;
        if (_lblName != null)
            _lblName.Text = CombatUnitFormat.DisplayName(definition?.DisplayNameId, character.DefinitionId);

        if (_presenter != null)
        {
            if (definition != null)
            {
                _presenter.Visible = true;
                _presenter.Bind(definition);
            }
            else
            {
                _presenter.Visible = false;
            }
        }

        if (_lblState != null)
        {
            var key = character.IsSealed ? "UI_COMBAT_SEALED" : character.HasActed ? "UI_COMBAT_CONFIRMED" : "";
            _lblState.Visible = key.Length > 0;
            _lblState.Text = key.Length > 0 ? Localization.Tr(key) : "";
        }
    }

    /// <summary>当前操控 / 可选目标的高亮：操控用强调色描边，目标用酒红。</summary>
    public void SetHighlight(bool controlled, bool targetable)
    {
        _targetable = targetable;
        if (_highlight == null)
            return;

        _highlight.Visible = controlled || targetable;
        _highlight.SelfModulate = targetable ? KemoPalette.Danger : KemoPalette.Accent;
        MouseDefaultCursorShape = targetable ? CursorShape.PointingHand : CursorShape.Arrow;
    }

    public void Play(string animName) => _presenter?.Play(animName);

    /// <summary>
    /// 移动到指定的全局坐标：由 <see cref="UnitTweens.MoveToAsync"/> 走 Godot 4.7 offset transform，
    /// 位移不写 <c>Position</c>，容器重排（排序 / 尺寸变化）不会冲掉动画。
    /// </summary>
    public Task MoveToAsync(Vector2 globalPosition, float duration)
    {
        ZIndex = 1;
        return UnitTweens.MoveToAsync(this, globalPosition, duration);
    }

    public async Task ReturnHomeAsync(float duration)
    {
        await UnitTweens.ReturnHomeAsync(this, duration);
        ZIndex = 0;
    }

    public Task AttackPulseAsync(float duration)
    {
        Play("attack");
        return UnitTweens.PulseAsync(this, 1.08f, duration);
    }

    public Task ShakeAsync(float duration) => UnitTweens.ShakeAsync(this, 8f, duration);
}