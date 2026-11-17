using KemoCard.Frame.Logging;
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

    public void Tick(double delta)
    {
        _cacheCheckAccumulator += delta;
        if (_cacheCheckAccumulator < 1) return;
        _cacheCheckAccumulator = 0;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // 快照：强制流转 / 销毁会同步修改 _map（Destroy handler 里 Remove），不能边枚举边改。
        UIVo[] snapshot = [.. _map.Values];
        foreach (var vo in snapshot) ForceCloseTimeout(vo, now);
        foreach (var vo in snapshot) DestroyExpiredCache(vo, now);
    }

    /// <summary>
    /// 关闭超时看门狗：OnCloseAnim 的 done 永不回调（Tween 被 kill / 节点离树 / 业务漏调）时，
    /// VO 会永久停在 Close（IsOpen=false、IsClose=true，既不会被缓存回收，也进不了批量关闭），
    /// 这里兜底推进到 CloseDone。
    /// </summary>
    private static void ForceCloseTimeout(UIVo vo, long now)
    {
        if (vo.StateMachine.CurrentState != EUIState.Close) return;
        if (vo.Lifecycle.CloseStartTime == 0) return;

        long elapsed = now - vo.Lifecycle.CloseStartTime;
        if (elapsed <= UIConsts.UI_CLOSE_TIMEOUT_MS) return;

        AppLog.Error($"UI 管理器: 关闭UI<{vo.Id}> 超时（{elapsed}ms），强制进入关闭完成。", "UI");
        vo.StateMachine.TransitionTo(EUIState.CloseDone, vo);
    }

    private void DestroyExpiredCache(UIVo vo, long now)
    {
        if (vo.StateMachine.CurrentState != EUIState.Cache) return;
        if (vo.Lifecycle.DestroyTime == -1 || now < vo.Lifecycle.DestroyTime) return;
        // 二次校验：期间可能已被销毁，或同名 id 已重建为另一个 VO。
        if (!_map.TryGetValue(vo.Id, out UIVo? current) || !ReferenceEquals(current, vo)) return;

        vo.StateMachine.TransitionTo(EUIState.Destroy, vo);
    }
}