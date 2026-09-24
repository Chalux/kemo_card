using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat.Buffs;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 手牌槽充能指示（战斗规格 §13.2）：环绕槽位的光晕边框 + 顶部「已打出 X / N」进度条。
/// 无充能 buff（<c>slot.charge</c>）时整体隐藏；进度 = <see cref="BuffInstance.ChargePlayed"/> / <see cref="BuffInstance.ChargeRequired"/>。
/// </summary>
public partial class SlotChargeCmp : BaseCmp
{
    private const float PulseMinAlpha = 0.55f;

    [Export] private Panel? _frame;
    [Export] private ProgressBar? _bar;
    [Export] private Label? _label;

    private Tween? _pulse;

    /// <summary>按模拟器状态对账：<paramref name="charge"/> 为 null（该槽无充能）时整体隐藏。</summary>
    public void Bind(BuffInstance? charge)
    {
        if (charge is null)
        {
            Visible = false;
            StopPulse();
            return;
        }

        Visible = true;
        var required = charge.ChargeRequired;
        var played = charge.ChargePlayed;

        if (_bar != null)
        {
            _bar.MaxValue = required;
            _bar.Value = played;
        }

        if (_label != null)
            _label.Text = string.Format(Localization.Tr("UI_COMBAT_SLOT_CHARGE"), played, required);

        StartPulse();
    }

    protected override void OnExitTree() => StopPulse();

    /// <summary>环绕光晕的呼吸动画；已在播时不重启，避免每次对账闪一下。</summary>
    private void StartPulse()
    {
        if (_frame is null || (_pulse is not null && _pulse.IsValid()))
            return;

        var glow = _frame.Modulate with { A = 1f };
        var dim = _frame.Modulate with { A = PulseMinAlpha };
        _frame.Modulate = dim;

        // Godot 4 的 Tween 没有 ping-pong 循环类型：亮 → 暗两条顺序播放 + 无限循环等效。
        _pulse = CreateTween().SetLoops(0);
        _pulse.TweenProperty(_frame, CanvasItem.PropertyName.Modulate.ToString(), glow, 0.9f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _pulse.TweenProperty(_frame, CanvasItem.PropertyName.Modulate.ToString(), dim, 0.9f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private void StopPulse()
    {
        _pulse?.Kill();
        _pulse = null;
        if (_frame != null)
            _frame.Modulate = _frame.Modulate with { A = 1f };
    }
}