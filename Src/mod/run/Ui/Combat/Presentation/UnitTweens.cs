using Godot;

namespace KemoCard.Mod.Run.Ui.CombatUi.Presentation;

/// <summary>
/// 单位节点通用的 Tween 小动作（位移 / 抖动 / 缩放脉冲 / 淡出 / 闪光）。
/// 全部以 <c>await</c> 形式返回，供 <see cref="CombatAnimator"/> 顺序编排；节点已失效时立即返回。
/// </summary>
public static class UnitTweens
{
    public static async Task MoveToAsync(Control node, Vector2 globalPosition, float duration)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
            return;

        var tween = node.CreateTween().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, Control.PropertyName.GlobalPosition.ToString(), globalPosition, duration);
        await node.ToSignal(tween, Tween.SignalName.Finished);
    }

    public static async Task PulseAsync(Control node, float scale, float duration)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
            return;

        var origin = node.Scale;
        node.PivotOffset = node.Size / 2f;
        var tween = node.CreateTween().SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, Control.PropertyName.Scale.ToString(), origin * scale, duration / 2f);
        tween.TweenProperty(node, Control.PropertyName.Scale.ToString(), origin, duration / 2f);
        await node.ToSignal(tween, Tween.SignalName.Finished);
    }

    public static async Task ShakeAsync(Control node, float amplitude, float duration)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
            return;

        var origin = node.Position;
        var tween = node.CreateTween();
        const int steps = 4;
        for (var i = 0; i < steps; i++)
        {
            var sign = i % 2 == 0 ? 1f : -1f;
            var falloff = 1f - (i / (float)steps);
            tween.TweenProperty(
                node,
                Control.PropertyName.Position.ToString(),
                origin + new Vector2(sign * amplitude * falloff, 0f),
                duration / (steps + 1));
        }

        tween.TweenProperty(node, Control.PropertyName.Position.ToString(), origin, duration / (steps + 1));
        await node.ToSignal(tween, Tween.SignalName.Finished);
    }

    public static async Task FadeAsync(CanvasItem node, float targetAlpha, float duration)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
            return;

        var target = node.Modulate with { A = targetAlpha };
        var tween = node.CreateTween();
        tween.TweenProperty(node, CanvasItem.PropertyName.Modulate.ToString(), target, duration);
        await node.ToSignal(tween, Tween.SignalName.Finished);
    }

    /// <summary>短暂提亮再复原（受击 / 球亮起 / 图标弹入的通用高亮）。</summary>
    public static async Task FlashAsync(CanvasItem node, Color flashColor, float duration)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
            return;

        var origin = node.Modulate;
        var tween = node.CreateTween();
        tween.TweenProperty(node, CanvasItem.PropertyName.Modulate.ToString(), flashColor, duration / 2f);
        tween.TweenProperty(node, CanvasItem.PropertyName.Modulate.ToString(), origin, duration / 2f);
        await node.ToSignal(tween, Tween.SignalName.Finished);
    }

    /// <summary>等待一段时间（走场景树计时器，暂停时随之暂停）。</summary>
    public static async Task WaitAsync(Node node, float seconds)
    {
        if (seconds <= 0f || !GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
            return;

        await node.ToSignal(node.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }
}