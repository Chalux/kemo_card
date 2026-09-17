using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.UI.States;

namespace KemoCard.Frame.UI;

/// <summary>
/// UIVo 注册表：管理 UIVo 的创建、查找、移除及缓存超时销毁。
/// 从 UIManager 中拆分出的独立组件。
/// </summary>
public sealed class UIVoRegistry(UIManager manager, IEnumerable<IStateHandler<EUIState, IUIStateContext>> handlers)
{
    private readonly Dictionary<string, UIVo> _map = [];
    private readonly IEnumerable<IStateHandler<EUIState, IUIStateContext>> _handlers = handlers;
    private readonly UIManager _manager = manager;
    private double _cacheCheckAccumulator;

    public IReadOnlyDictionary<string, UIVo> Map => _map;

    public UIVo GetOrCreate(string id, EUIType type, string ownerModId, object? payload)
    {
        if (_map.TryGetValue(id, out UIVo? vo))
        {
            vo.Payload = payload;
            return vo;
        }

        vo = new UIVo(id, type, ownerModId, payload, _manager, _handlers);
        _map[id] = vo;
        return vo;
    }

    public UIVo? Get(string id)
    {
        return _map.TryGetValue(id, out var vo) ? vo : null;
    }

    public void Remove(string id)
    {
        _map.Remove(id);
    }

    public void TickCacheDestroy(double delta)
    {
        _cacheCheckAccumulator += delta;
        if (_cacheCheckAccumulator < 1) return;
        _cacheCheckAccumulator = 0;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        List<string> toDestroy = [];

        foreach (var (id, vo) in _map)
        {
            if (vo.StateMachine.CurrentState != EUIState.Cache || vo.Lifecycle.DestroyTime == -1)
                continue;

            if (now >= vo.Lifecycle.DestroyTime)
                toDestroy.Add(id);
        }

        foreach (var id in toDestroy)
        {
            _map[id].StateMachine.TransitionTo(EUIState.Destroy,
                new UIStateContext(_map[id], _manager));
        }
    }
}