using Godot;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.UI.States;

namespace KemoCard.Frame.UI;

/// <summary>
/// UI 打开协调器：管理打开队列、当前打开项、并发控制及加载超时检查。
/// 从 UIManager 中拆分出的独立组件。
/// </summary>
public sealed class UIOpenCoordinator(UIManager manager)
{
    private readonly Queue<UIVo> _openQueue = [];
    private UIVo? _currOpening;
    private readonly UIManager _manager = manager;

    public UIVo? CurrentOpening => _currOpening;

    public void EnqueueOpen(UIVo vo)
    {
        UIVo[] arr = [.. _openQueue];
        _openQueue.Clear();
        foreach (var item in arr)
        {
            if (item != vo) _openQueue.Enqueue(item);
        }
        if (_currOpening == vo) _currOpening = null;
        _openQueue.Enqueue(vo);
        OpenNext();
    }

    public void OpenNext()
    {
        if (_currOpening != null)
        {
            switch (_currOpening.StateMachine.CurrentState)
            {
                case EUIState.Load:
                case EUIState.PreLoad:
                    return;
                default:
                    _currOpening = null;
                    OpenNext();
                    return;
            }
        }

        while (_openQueue.Count > 0)
        {
            UIVo vo = _openQueue.Peek();
            _currOpening = vo;
            vo.StateMachine.TransitionTo(EUIState.Load, new UIStateContext(vo, _manager));
            return;
        }
    }

    public void RemoveFromQueue(UIVo vo)
    {
        UIVo[] arr = [.. _openQueue];
        _openQueue.Clear();
        foreach (var item in arr)
        {
            if (item != vo) _openQueue.Enqueue(item);
        }
    }

    public void CheckLoadTimeout()
    {
        if (_currOpening == null) return;

        switch (_currOpening.StateMachine.CurrentState)
        {
            case EUIState.Load:
            case EUIState.PreLoad:
            {
                if (_currOpening.Lifecycle.LoadTime == 0) break;

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (now - _currOpening.Lifecycle.LoadTime > UIConsts.UI_LOAD_TIMEOUT)
                {
                    _currOpening.StateMachine.TransitionTo(EUIState.Destroy,
                        new UIStateContext(_currOpening, _manager));
                    _currOpening = null;
                    OpenNext();
                }
                break;
            }
        }
    }

    public void ResetCurrentOpening(UIVo vo)
    {
        if (_currOpening == vo) _currOpening = null;
    }
}
