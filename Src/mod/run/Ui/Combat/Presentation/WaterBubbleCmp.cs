using Godot;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Run.Ui.CombatUi.Presentation;

/// <summary>敌方命中水泡：沿弧线飞行，命中后散成水滴；只接收舞台坐标。</summary>
public partial class WaterBubbleCmp : BaseCmp
{
    private const float Radius = 18f;
    private readonly TaskCompletionSource _completion = new();
    private Tween? _tween;
    private Vector2 _start;
    private Vector2 _end;
    private Vector2 _direction;
    private float _flight;
    private float _burst;
    private bool _bursting;

    #region 播放与清理

    public Task PlayAsync(Vector2 fromGlobal, Vector2 toGlobal)
    {
        if (!IsInsideTree())
            return Task.CompletedTask;

        // 在特效层的局部空间插值，兼容战斗窗口的缩放与偏移。
        var inverse = ((CanvasItem)GetParent()).GetGlobalTransform().AffineInverse();
        _start = inverse * fromGlobal;
        _end = inverse * toGlobal;
        _direction = (_end - _start).Normalized();
        Position = _start;
        QueueRedraw();
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(SetFlight), 0f, 1f, CombatAnimationTiming.EnemyBubbleFlight);
        _tween.TweenMethod(Callable.From<float>(SetBurst), 0f, 1f, CombatAnimationTiming.EnemyBubbleBurst);
        _tween.TweenCallback(Callable.From(Finish));
        return _completion.Task;
    }

    private void SetFlight(float progress)
    {
        _flight = progress;
        var height = MathF.Min(70f, _start.DistanceTo(_end) * 0.12f);
        Position = _start.Lerp(_end, progress) + Vector2.Up * (MathF.Sin(progress * Mathf.Pi) * height);
        QueueRedraw();
    }

    private void SetBurst(float progress)
    {
        _bursting = true;
        _burst = progress;
        Position = _end;
        QueueRedraw();
    }

    private void Finish()
    {
        _completion.TrySetResult();
        QueueFree();
    }

    protected override void OnExitTree()
    {
        _tween?.Kill();
        _completion.TrySetResult();
        base.OnExitTree();
    }

    #endregion

    #region 水泡与水滴绘制

    public override void _Draw()
    {
        if (_bursting)
        {
            var alpha = 1f - _burst;
            DrawArc(Vector2.Zero, Radius + _burst * 24f, 0f, Mathf.Tau, 48,
                new Color(0.55f, 0.91f, 1f, alpha * 0.8f), 2f, true);
            for (var i = 0; i < 8; i++)
            {
                var direction = Vector2.FromAngle(i * Mathf.Tau / 8f);
                var center = direction * (Radius + _burst * 32f);
                DrawCircle(center, 3f * alpha + 0.5f, new Color(0.4f, 0.84f, 1f, alpha), true, -1f, true);
            }
            return;
        }

        var radius = Radius * (1f + MathF.Sin(_flight * Mathf.Tau * 2f) * 0.07f);
        for (var i = 1; i <= 3; i++)
        {
            var center = -_direction * (radius + i * 12f) + Vector2.Up * MathF.Sin(_flight * 12f + i) * 3f;
            DrawCircle(center, 6f - i, new Color(0.5f, 0.87f, 1f, 0.26f * (1f - i / 4f)), true, -1f, true);
        }
        DrawCircle(Vector2.Zero, radius + 5f, new Color(0.28f, 0.76f, 1f, 0.09f), true, -1f, true);
        DrawCircle(Vector2.Zero, radius, new Color(0.26f, 0.72f, 1f, 0.32f), true, -1f, true);
        DrawArc(Vector2.Zero, radius, 0f, Mathf.Tau, 48, new Color(0.62f, 0.94f, 1f, 0.95f), 2f, true);
        DrawArc(Vector2.Zero, radius - 4f, Mathf.Pi * 1.08f, Mathf.Pi * 1.62f, 16,
            new Color(0.92f, 0.99f, 1f, 0.9f), 2.5f, true);
        DrawCircle(new Vector2(-5f, -7f), 2f, Colors.White, true, -1f, true);
    }

    #endregion
}