using Godot;

namespace KemoCard.Frame.Notification;

/// <summary>
/// 红点视觉组件。Node2D + TextureRect 子节点，通过 WatchRed 订阅指定红点 ID，
/// 状态变更时自动驱动显示/隐藏。
/// <para>默认不可见（_Ready 中 Visible = false），仅当 WatchRed 订阅的节点激活时显示。</para>
/// </summary>
public partial class RedDotCmp : Node2D
{
    private string? _watchedId;

    public override void _Ready()
    {
        Visible = false;
    }

    /// <summary>
    /// 离开场景树时必须反订阅：<see cref="RedDotService.OnStateChanged"/> 是静态强引用事件，
    /// 不解绑会把已 <c>QueueFree</c> 的节点永久留在委托链上，并在节点释放后继续被回调 <c>Visible</c>。
    /// </summary>
    public override void _ExitTree()
    {
        if (_watchedId != null)
        {
            RedDotService.OnStateChanged -= OnRedStateChanged;
            _watchedId = null;
        }
    }

    /// <summary>
    /// 订阅指定红点节点。切换 id 时自动取消旧订阅并同步新状态。
    /// 传入 null 取消订阅并隐藏。
    /// </summary>
    public void WatchRed(string? redDotId)
    {
        if (_watchedId == redDotId)
        {
            return;
        }

        if (_watchedId != null)
        {
            RedDotService.OnStateChanged -= OnRedStateChanged;
        }

        _watchedId = redDotId;

        if (redDotId != null)
        {
            RedDotService.OnStateChanged += OnRedStateChanged;
            Visible = RedDotService.IsActive(redDotId);
        }
        else
        {
            Visible = false;
        }
    }

    private void OnRedStateChanged(string id, bool active)
    {
        if (id == _watchedId)
        {
            Visible = active;
        }
    }
}