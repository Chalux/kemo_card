using Godot;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.UI;

namespace KemoCard.Frame.Notification;

/// <summary>
/// 红点视觉组件。Node2D + TextureRect 子节点，通过 WatchRed 订阅指定红点 ID，
/// 状态变更时自动驱动显示/隐藏。
/// <para>默认不可见，仅当 WatchRed 订阅的节点激活时显示（_Ready 与 _EnterTree 都会按当前状态对账）。</para>
/// <para>订阅经 <see cref="BindingScope"/> + <see cref="EventSubscribeAttribute"/> 收口：
/// 进树登记、离场统一解绑，不再手写静态事件增减。</para>
/// </summary>
public partial class RedDotCmp : Node2D
{
    private readonly BindingScope _binder = new();
    private string? _watchedId;

    public override void _EnterTree()
    {
        base._EnterTree();
        _binder.BindDecorated(RedDotService.Events, this);

        // 离场期间状态可能已变化：重新进树时主动对账一次。
        if (_watchedId is not null)
        {
            Visible = RedDotService.IsActive(_watchedId);
        }
    }

    /// <summary>
    /// 框架唯一离场入口。<b>sealed：子类不得 override</b> — 请改 override <see cref="OnExitTree"/>。
    /// </summary>
    public sealed override void _ExitTree()
    {
        try { OnExitTree(); }
        finally
        {
            _binder.UnbindAll();
            // 保留 _watchedId：缓存重开时 _Ready 不会重跑，_EnterTree 需要靠它重新对账。
            // 节点若被复用到别的数据项，必须显式调用 WatchRed(null) 或 WatchRed(newId)。
            base._ExitTree();
        }
    }

    /// <summary>框架级离场生命周期：子类只做非订阅类清理。</summary>
    protected virtual void OnExitTree() { }

    public override void _Ready()
    {
        // WatchRed 可能早于入树调用；_Ready 在首次入树后执行，不能无条件隐藏。
        Visible = _watchedId is not null && RedDotService.IsActive(_watchedId);
    }

    /// <summary>
    /// 订阅指定红点节点。传入 null 取消监听效果并隐藏。
    /// </summary>
    public void WatchRed(string? redDotId)
    {
        if (_watchedId == redDotId)
        {
            return;
        }

        _watchedId = redDotId;
        Visible = redDotId != null && RedDotService.IsActive(redDotId);
    }

    /// <summary>
    /// 停止监听并隐藏。缓存重开会保留 <see cref="_watchedId"/> 以重新对账；
    /// 池化/换行复用的节点必须显式调用本方法（或重新 WatchRed）清除旧数据项。
    /// </summary>
    public void ResetWatch() => WatchRed(null);

    /// <remarks>
    /// 事件总线会把「自身变化」以及「父级 / 子级变化」都投递给本节点；
    /// 这里只按目标 id 过滤，重复通知是幂等赋值。
    /// </remarks>
    [EventSubscribe(ERedDotEvent.Changed)]
    private void OnRedChanged(RedDotChangedPayload payload)
    {
        if (payload.Id == _watchedId)
        {
            Visible = payload.Active;
        }
    }
}