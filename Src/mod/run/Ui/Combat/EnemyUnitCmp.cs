using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Global.Ui.Comp;
using KemoCard.Mod.Global.Ui.Themes;
using KemoCard.Mod.Global.Ui.Tip;
using KemoCard.Mod.Run.Ui.CombatUi.Presentation;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 战场上的敌人：边框占位（<c>EnemyDto</c> 尚无序列帧字段，<see cref="BindPresentation"/> 预留接口）
/// + 常驻血条 + buff 列表；悬停显示属性摘要；选目标态可点击。
/// </summary>
public partial class EnemyUnitCmp : BaseCmp
{
    [Export] private CharacterPresenter? _presenter;
    [Export] private Label? _lblName;
    [Export] private HpBarCmp? _hp;
    [Export] private BuffListCmp? _buffs;
    [Export] private Panel? _highlight;

    /// <summary>点击回调（选目标态），参数为敌人索引。</summary>
    public Action<int>? Clicked { get; set; }

    public int EnemyIndex { get; private set; } = -1;
    public HpBarCmp? HpBar => _hp;
    public BuffListCmp? BuffList => _buffs;

    private EnemyUnit? _unit;
    private EnemyDto? _definition;
    private bool _targetable;

    protected override void OnReady()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    protected override void InitEvent()
    {
        OnClicks(this, () =>
        {
            if (_targetable && EnemyIndex >= 0)
                Clicked?.Invoke(EnemyIndex);
        });
        Binder.OnMouseEnterExit(this, OnHoverEntered, OnHoverExited);
    }

    protected override void OnExitTree()
    {
        KeywordTipService.Current?.HideTips(this);
    }

    public void Bind(int enemyIndex, EnemyUnit unit, EnemyDto? definition)
    {
        EnemyIndex = enemyIndex;
        _unit = unit;
        _definition = definition;
        if (_lblName != null)
            _lblName.Text = CombatUnitFormat.DisplayName(definition?.DisplayNameId, unit.DefinitionId);

        // 敌人侧尚无序列帧配置：这里恒为 null，保持空白边框。字段落地后由调用方传入。
        BindPresentation(null);
        Refresh();
    }

    /// <summary>与模拟器对账：血条终值、buff 列表、阵亡态。</summary>
    /// <remarks>
    /// 阵亡即退场（2026-09-26）：不留在场上当幽灵——<see cref="Visible"/> 置 false 后
    /// 所在 HBox 会让存活敌人重新排布；<see cref="FadeOutAsync"/> 先把当前实例淡出，随后这里收尾隐藏。
    /// </remarks>
    public void Refresh()
    {
        if (_unit is null)
            return;

        _hp?.SetValue(_unit.CurrentHp, _unit.MaxHp);
        _buffs?.Bind(_unit.Buffs.Visible);
        Visible = _unit.IsAlive;
        if (_unit.IsAlive)
            Modulate = Modulate with { A = 1f };
    }

    /// <summary>
    /// 序列帧接口预留：传入 <see cref="CharacterPresentationDto"/> 即可播放（复用角色的 <see cref="CharacterPresenter"/>）。
    /// 当前内容侧 <c>EnemyDto</c> 无该字段，传 null 时只显示边框占位。
    /// </summary>
    public void BindPresentation(CharacterPresentationDto? presentation)
    {
        if (_presenter == null)
            return;

        if (presentation is null || _unit is null)
        {
            _presenter.Visible = false;
            return;
        }

        _presenter.Visible = true;
        _presenter.Bind(new CharacterDto { Id = _unit.DefinitionId, Presentation = presentation });
    }

    public void SetTargetable(bool targetable)
    {
        _targetable = targetable && (_unit?.IsAlive ?? false);
        if (_highlight != null)
        {
            _highlight.Visible = _targetable;
            _highlight.SelfModulate = KemoPalette.Danger;
        }

        MouseDefaultCursorShape = _targetable ? CursorShape.PointingHand : CursorShape.Arrow;
    }

    public Task ShakeAsync(float duration) => UnitTweens.ShakeAsync(this, 8f, duration);

    public Task LungeAsync(float duration) => UnitTweens.PulseAsync(this, 1.08f, duration);

    /// <summary>阵亡退场：淡出到完全透明（随后的 <see cref="Refresh"/> 隐藏并释放占位）。</summary>
    public Task FadeOutAsync(float duration) => UnitTweens.FadeAsync(this, 0f, duration);

    #region 悬停属性摘要

    private void OnHoverEntered()
    {
        if (_unit is null)
            return;

        var title = CombatUnitFormat.DisplayName(_definition?.DisplayNameId, _unit.DefinitionId);
        var lines = new List<string>();
        var identity = CombatUnitFormat.RaceAndRole(_unit.Race, _definition?.Role ?? ERole.None);
        if (identity.Length > 0)
            lines.Add(identity);
        lines.Add(string.Format(Localization.Tr("UI_COMBAT_TIP_HP"), _unit.CurrentHp, _unit.MaxHp));
        lines.Add(string.Format(Localization.Tr("UI_COMBAT_TIP_ATTACK"), CombatUnitFormat.Attack(_unit.Asc)));
        lines.Add(string.Format(Localization.Tr("UI_COMBAT_TIP_DEFENSE"), CombatUnitFormat.Defense(_unit.Asc)));

        KeywordTipService.Current?.ShowCustomTips(this, [(title, string.Join("\n", lines))], TipSide.Left);
    }

    private void OnHoverExited() => KeywordTipService.Current?.HideTips(this);

    #endregion
}