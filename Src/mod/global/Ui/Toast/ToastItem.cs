using Godot;

namespace KemoCard.Mod.Global.Ui.Toast;

/// <summary>
/// 单条 Toast 节点：负责文案填充与「停留后上移淡出」动画，动画完成回调交给池回收。
/// </summary>
public partial class ToastItem : Control
{
    [Export] private Label? _lblText;

    public override void _Ready()
    {
        Modulate = Colors.White;
    }

    /// <summary>
    /// 填充翻译后的文案并立即显示（无淡入）。
    /// </summary>
    public void ShowText(string text)
    {
        Visible = true;
        Modulate = Colors.White;
        if (_lblText != null)
        {
            _lblText.Text = text;
        }
    }

    /// <summary>
    /// 播放生命周期动画：延迟 <paramref name="stayMs"/> 后，并行上移 <paramref name="risePx"/> 与淡出 <paramref name="fadeMs"/>。
    /// 完成后回调 <paramref name="onRecycle"/>。
    /// </summary>
    public void PlayRecycle(float stayMs, float fadeMs, float risePx, Action onRecycle)
    {
        var tween = CreateTween();
        tween.TweenInterval(stayMs / 1000f);
        tween.SetParallel();
        tween.TweenProperty(this, "modulate:a", 0f, fadeMs / 1000f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(this, "position:y", Position.Y - risePx, fadeMs / 1000f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        tween.Chain().TweenCallback(Callable.From(onRecycle));
    }
}