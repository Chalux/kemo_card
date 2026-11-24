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
    private Color? _fillColor;
    private Color? _underlayColor;

    public int Current => _current;
    public int Max => _max;

    #region 血条颜色与数值

    /// <summary>当前管覆盖下一管的满血底色；最后一管的底层完全透明。</summary>
    public void SetLifeBarColors(int index, int count)
    {
        SetFillColor(LifeBarColor(index, count));
        var underlayColor = count - index > 1 ? LifeBarColor(index + 1, count) : Colors.Transparent;
        if (_bar is null || _underlayColor == underlayColor)
            return;

        _underlayColor = underlayColor;
        StyleBox background;
        if (underlayColor.A == 0f)
            background = new StyleBoxEmpty();
        else
        {
            // 底层与当前管共用形状，各自复制样式，受伤时露出下一管的颜色。
            var fill = _bar.GetThemeStylebox("fill").Duplicate() as StyleBoxFlat ?? new StyleBoxFlat();
            fill.BgColor = underlayColor;
            background = fill;
        }
        _bar.AddThemeStyleboxOverride("background", background);
    }

    /// <summary>复制主题样式后改色，避免影响共用此血条的队伍和其他敌人。</summary>
    public void SetFillColor(Color color)
    {
        if (_bar is null || _fillColor == color)
            return;
        _fillColor = color;
        var fill = _bar.GetThemeStylebox("fill").Duplicate() as StyleBoxFlat ?? new StyleBoxFlat();
        fill.BgColor = color;
        _bar.AddThemeStyleboxOverride("fill", fill);
        _lblValue?.AddThemeColorOverride("font_color", Colors.White);
        _lblValue?.AddThemeColorOverride("font_outline_color", Colors.Black);
        _lblValue?.AddThemeConstantOverride("outline_size", 3);
    }

    public static Color LifeBarColor(int index, int count) => Math.Clamp(count - index - 1, 0, 4) switch
    {
        0 => Colors.White,
        1 => new Color("56c779"),
        2 => new Color("f2a33a"),
        3 => new Color("b878e0"),
        _ => new Color("e85a5a"),
    };

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

    public void SetMarbleForecast(float remainingHealth)
    {
        if (_lblValue != null)
            _lblValue.Text = $"{_current} → {remainingHealth:0.##} / {_max}";
    }

    /// <summary>清除弹珠预计伤害预览，恢复为「当前 / 最大」。</summary>
    public void ClearMarbleForecast() => RefreshLabel();

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

    #endregion
}