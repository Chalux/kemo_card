using Godot;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 血条：进度条 + 「当前 / 最大」文字。<see cref="SetValue"/> 立即写入（对账用），
/// <see cref="AnimateToAsync"/> 由动画驱动过渡到目标值。
/// </summary>
public partial class HpBarCmp : BaseCmp
{
    [Export] private ProgressBar? _bar;
    [Export] private Label? _lblValue;

    private int _current;
    private int _max;

    public int Current => _current;
    public int Max => _max;

    public void SetValue(int current, int max)
    {
        _current = Math.Max(0, current);
        _max = Math.Max(1, max);
        if (_bar != null)
        {
            _bar.MaxValue = _max;
            _bar.Value = _current;
        }

        RefreshLabel();
    }

    public async Task AnimateToAsync(int current, int max, float duration)
    {
        _current = Math.Max(0, current);
        _max = Math.Max(1, max);
        RefreshLabel();
        if (_bar == null || !IsInsideTree() || duration <= 0f)
        {
            SetValue(_current, _max);
            return;
        }

        _bar.MaxValue = _max;
        var tween = CreateTween().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_bar, Godot.Range.PropertyName.Value.ToString(), (double)_current, duration);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private void RefreshLabel()
    {
        if (_lblValue != null)
            _lblValue.Text = $"{_current} / {_max}";
    }
}