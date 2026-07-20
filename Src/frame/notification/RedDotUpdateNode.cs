using Godot;

namespace KemoCard.Frame.Notification;

/// <summary>
/// 内部辅助 Node，挂到 SceneTree.Root 上，提供 _FlushAll 方法
/// 作为 CallDeferred 的载体，实现延迟一帧批处理。
/// 无需 _Process，Godot 自动对同一方法名的 CallDeferred 去重。
/// </summary>
internal sealed partial class RedDotUpdateNode : Node
{
    public void _FlushAll()
    {
        RedDotService.InternalFlushAll();
    }
}
