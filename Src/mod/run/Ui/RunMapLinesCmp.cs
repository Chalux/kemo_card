using Godot;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Run.Ui;

public partial class RunMapLinesCmp : BaseCmp
{
    /// <summary>会改变路线按钮位置/宽度的容器；它们重排时必须跟着重绘。</summary>
    private static readonly string[] LayoutSources =
    [
        "Routes",
        "Routes/Lanes",
        "Routes/Lanes/Branch0",
        "Routes/Lanes/Branch1",
    ];

    private static readonly Color LineColor = new(0.65f, 0.55f, 0.4f, 0.7f);

    /// <summary>重绘请求次数，供回归测试断言「容器重排确实带动了重绘」。</summary>
    internal int RedrawRequests { get; private set; }

    protected override void InitEvent()
    {
        Binder.OnResized(this, RequestRedraw);
        // 节点按钮的宽度要等 RunAdventureCmp.Bind() 写入真实文本、容器重排之后才撑开。
        // 只听自身 resized 会让连线停在首次布局的旧坐标上：canvas_items 拉伸下改窗口
        // 不会改变控件尺寸，也就不会再触发任何 resized。故必须订阅容器的重排。
        var map = GetParent();
        foreach (var path in LayoutSources)
        {
            if (map?.GetNodeOrNull<Container>(path) is { } source)
            {
                Binder.OnSortChildren(source, RequestRedraw);
            }
        }

        RequestRedraw();
    }

    private void RequestRedraw()
    {
        RedrawRequests++;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (GetParent() is not Control map)
        {
            return;
        }

        for (var branch = 0; branch < 2; branch++)
        {
            var wiring = ComputeWiring(branch, map);
            DrawLine(wiring.Start, wiring.Select, LineColor, 2f, true);
            // 节点属于同一路线，可自由选择；不在节点之间画顺序连线。
            DrawRect(wiring.Frame, LineColor, false, 2f);
            DrawLine(wiring.Select, wiring.FrameLeft, LineColor, 2f, true);
            DrawLine(wiring.FrameRight, wiring.Boss, LineColor, 2f, true);
        }
    }

    /// <summary>一条路线的连线：起点 → 选择按钮 → 该路线的节点框 → BOSS（均为本节点局部坐标）。</summary>
    internal readonly record struct Wiring(
        Vector2 Start,
        Vector2 Select,
        Rect2 Frame,
        Vector2 FrameLeft,
        Vector2 FrameRight,
        Vector2 Boss);

    /// <summary>按当前布局算出连线几何；<see cref="_Draw"/> 与回归测试共用同一份来源。</summary>
    internal Wiring ComputeWiring(int branch, Control map)
    {
        var inverse = GetGlobalTransform().AffineInverse();
        Vector2 Center(string path)
        {
            var control = map.GetNode<Control>(path);
            return inverse * (control.GetGlobalTransform() * (control.Size / 2f));
        }

        var select = Center($"Routes/Lanes/Branch{branch}/Select");
        var first = map.GetNode<Control>($"Routes/Lanes/Branch{branch}/Node0");
        var last = map.GetNode<Control>($"Routes/Lanes/Branch{branch}/Node2");
        var topLeft = inverse * first.GlobalPosition - new Vector2(8, 8);
        var bottomRight = inverse * (last.GetGlobalTransform() * last.Size) + new Vector2(8, 8);
        var frame = new Rect2(topLeft, bottomRight - topLeft);

        return new Wiring(
            Center("Routes/Start"),
            select,
            frame,
            new Vector2(frame.Position.X, frame.GetCenter().Y),
            new Vector2(frame.End.X, frame.GetCenter().Y),
            Center("Routes/Boss"));
    }
}
