using Godot;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Run.Ui.CombatUi.Presentation;

/// <summary>伤害 / 治疗飘字：在指定全局位置出现，上浮并淡出后自毁。</summary>
public partial class DamageNumberCmp : BaseCmp
{
    [Export] private Label? _label;

    public async Task PlayAsync(Vector2 globalCenter, string text, Color color, float duration)
    {
        if (_label != null)
        {
            _label.Text = text;
            _label.AddThemeColorOverride("font_color", color);
        }

        // 先让 Label 按文字算出尺寸，再居中到目标点。
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree())
            return;

        GlobalPosition = globalCenter - Size / 2f;
        Modulate = Modulate with { A = 1f };

        var tween = CreateTween().SetParallel();
        tween.TweenProperty(this, Control.PropertyName.Position.ToString(), Position + new Vector2(0f, -40f), duration)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(this, CanvasItem.PropertyName.Modulate.ToString(), Modulate with { A = 0f }, duration)
            .SetDelay(duration * 0.4f);
        await ToSignal(tween, Tween.SignalName.Finished);
        QueueFree();
    }
}