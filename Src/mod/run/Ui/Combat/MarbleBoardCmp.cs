using System.Globalization;
using Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat.Marble;
using KemoCard.Mod.Global.Ui.Themes;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 弹珠盘面视图：把逻辑坐标等比映射到控件矩形，绘制墙壁、开放底口、四枚撞钉、
/// 发射口与场上活跃球。出牌阶段与大盘预览共用同一视图。
/// </summary>
/// <remarks>
/// 只读模拟器状态，不重算任何战斗结果。飞行期间按帧轮询球位置重绘（与
/// 「界面每帧读取球位置」的表现边界一致）；静止时由 <see cref="Bind"/> 驱动重绘。
/// </remarks>
public partial class MarbleBoardCmp : BaseCmp
{
    private MarbleRuntime? _runtime;
    private Vector2? _aimPointer;

    public MarbleRuntime? Runtime => _runtime;

    /// <summary>
    /// 弹珠阶段主盘面为 true：左键按指针方向发射、右键取消瞄准。
    /// 缩略盘面与大屏预览保持 false，只显示轨迹预览，不产生任何指令。
    /// </summary>
    public bool Interactive { get; set; }

    /// <summary>玩家在主盘面按下左键：参数为发射角度（弧度）。只在 <see cref="Interactive"/> 且可发射时触发。</summary>
    public Action<float>? LaunchRequested { get; set; }

    /// <summary>瞄准被取消（右键 / ESC）后触发，供界面刷新提示。</summary>
    public Action? AimCancelled { get; set; }

    public bool HasAim => _aimPointer is not null;

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseMotion motion:
                _aimPointer = motion.Position;
                QueueRedraw();
                break;
            case InputEventMouseButton { Pressed: true } button:
                if (button.ButtonIndex == MouseButton.Right)
                {
                    CancelAim();
                    AcceptEvent();
                    return;
                }

                if (button.ButtonIndex != MouseButton.Left || !Interactive)
                    return;

                _aimPointer = button.Position;
                QueueRedraw();
                if (_runtime?.CanLaunch == true && TryResolveLaunchAngle(button.Position, out var angle))
                    LaunchRequested?.Invoke(angle);
                AcceptEvent();
                break;
        }
    }

    /// <summary>清空指针瞄准（轨迹预览随之隐藏，下一颗球需要重新移动指针瞄准）。</summary>
    public void CancelAim()
    {
        if (_aimPointer is null)
            return;

        _aimPointer = null;
        QueueRedraw();
        AimCancelled?.Invoke();
    }

    public void Bind(MarbleRuntime? runtime)
    {
        _runtime = runtime;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // 有活跃球时保持逐帧刷新；没有活跃球时布局只随状态同步变化，无需空转重绘。
        if (_runtime is { ActiveBalls.Count: > 0 } && IsVisibleInTree())
            QueueRedraw();
    }

    public override void _Draw()
    {
        var runtime = _runtime;
        var config = runtime?.Config ?? MarblePhysicsConfig.Default;
        var (scale, origin) = ComputeTransform(config);

        Vector2 Map(MarbleVec point) => origin + new Vector2(point.X * scale, point.Y * scale);

        var topLeft = Map(MarbleVec.Zero);
        var topRight = Map(new MarbleVec(config.Width, 0f));
        var bottomLeft = Map(new MarbleVec(0f, config.Height));
        var bottomRight = Map(new MarbleVec(config.Width, config.Height));

        // 盘面底色 + 三面墙；底部用虚线表示开放离盘出口。
        DrawRect(new Rect2(topLeft, new Vector2(config.Width * scale, config.Height * scale)), WithAlpha(KemoPalette.SurfaceSunken, 0.32f));
        DrawLine(topLeft, topRight, KemoPalette.PanelBorder, 2f, true);
        DrawLine(topLeft, bottomLeft, KemoPalette.PanelBorder, 2f, true);
        DrawLine(topRight, bottomRight, KemoPalette.PanelBorder, 2f, true);
        DrawDashedLine(bottomLeft, bottomRight, WithAlpha(KemoPalette.AccentDim, 0.9f), 1f, 5f, true, true);

        // 悬空发射口。
        var launch = Map(config.LaunchOrigin);
        if (MouseFilter != MouseFilterEnum.Ignore && runtime?.CanLaunch == true && _aimPointer is { } pointer)
        {
            var direction = pointer - launch;
            if (!direction.IsZeroApprox() && runtime.PredictNextLaunch(direction.Angle(), config.Height) is { } path)
            {
                if (path.PathPoints.Count > 1)
                    DrawPolyline(path.PathPoints.Select(Map).ToArray(), WithAlpha(KemoPalette.Accent, 0.7f), 1.5f, true);
                if (path.FirstCollisionPoint is { } hit)
                    DrawArc(Map(hit), runtime.Queue[0].Radius * scale, 0f, Mathf.Tau, 24, KemoPalette.Accent, 2f, true);
            }
        }

        var launchRadius = MathF.Max(2f, config.BallRadius * scale);
        DrawCircle(launch, launchRadius, WithAlpha(KemoPalette.Accent, 0.9f), true, -1f, true);
        DrawArc(launch, launchRadius + 2f, 0f, Mathf.Tau, 24, WithAlpha(KemoPalette.AccentDim, 0.9f), 1f, true);

        var font = GetThemeDefaultFont();
        if (runtime is not null)
        {
            foreach (var peg in runtime.Pegs)
            {
                var center = Map(peg.Position);
                var radius = MathF.Max(1f, peg.Radius * scale);
                DrawCircle(center, radius, WithAlpha(KemoPalette.Accent, 0.78f), true, -1f, true);
                DrawArc(center, radius, 0f, Mathf.Tau, 32, KemoPalette.PanelBorder, 1.5f, true);
                if (font is null)
                    continue;

                if (radius < 5f)
                {
                    DrawString(font, center + new Vector2(radius + 2f, 3f),
                        peg.Points.ToString(CultureInfo.InvariantCulture), fontSize: 10,
                        modulate: GetThemeColor("font_color", "Label"));
                    continue;
                }

                var fontSize = Mathf.Clamp((int)(radius * 1.25f), 8, 18);
                DrawString(
                    font,
                    new Vector2(center.X - radius, center.Y + fontSize * 0.35f),
                    peg.Points.ToString(CultureInfo.InvariantCulture),
                    HorizontalAlignment.Center,
                    radius * 2f,
                    fontSize,
                    KemoPalette.TextOnAccent);
            }

            foreach (var ball in runtime.ActiveBalls)
            {
                var center = Map(ball.Position);
                var radius = MathF.Max(1f, ball.Radius * scale);
                DrawCircle(center, radius, WithAlpha(KemoPalette.Danger, 0.9f), true, -1f, true);
                DrawArc(center, radius, 0f, Mathf.Tau, 32, KemoPalette.PanelBorder, 1.5f, true);
                if (ball.IsStuckWarning)
                    DrawArc(center, radius + 2.5f, 0f, Mathf.Tau, 32, WithAlpha(KemoPalette.Danger, 0.65f), 2f, true);
            }
        }
    }

    /// <summary>把控件内坐标换算成发射角度：与绘制共用同一套等比映射，点击位置即瞄准方向。</summary>
    private bool TryResolveLaunchAngle(Vector2 local, out float angleRadians)
    {
        angleRadians = 0f;
        var runtime = _runtime;
        if (runtime is null)
            return false;

        var config = runtime.Config;
        var (scale, origin) = ComputeTransform(config);
        if (scale <= 0f)
            return false;

        var logicX = (local.X - origin.X) / scale;
        var logicY = (local.Y - origin.Y) / scale;
        var dx = logicX - config.LaunchOrigin.X;
        var dy = logicY - config.LaunchOrigin.Y;
        if (MathF.Abs(dx) < 0.001f && MathF.Abs(dy) < 0.001f)
            return false;

        angleRadians = MathF.Atan2(dy, dx);
        return float.IsFinite(angleRadians);
    }

    /// <summary>盘面逻辑坐标 → 控件矩形的等比映射（居中、留 4px 边距）；绘制与指针命中共用。</summary>
    private (float Scale, Vector2 Origin) ComputeTransform(MarblePhysicsConfig config)
    {
        const float padding = 4f;
        var availableWidth = MathF.Max(1f, Size.X - padding * 2f);
        var availableHeight = MathF.Max(1f, Size.Y - padding * 2f);
        var scale = MathF.Min(availableWidth / config.Width, availableHeight / config.Height);
        var origin = new Vector2(
            (Size.X - config.Width * scale) * 0.5f,
            (Size.Y - config.Height * scale) * 0.5f);
        return (scale, origin);
    }

    private static Color WithAlpha(Color color, float alpha) => new(color.R, color.G, color.B, alpha);
}
