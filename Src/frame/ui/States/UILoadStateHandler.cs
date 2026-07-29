using Godot;
using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.Util;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.UI.States;

/// <summary>
/// 加载 UI 状态处理器: 异步加载场景 + 遮罩资源
/// </summary>
public sealed class UILoadStateHandler : IStateHandler<EUIState, IUIStateContext>
{
    public EUIState State => EUIState.Load;

    public Action<EUIState, IUIStateContext?, object?>? OnEnter => OnEnterFunc;
    public Action<EUIState, IUIStateContext?>? OnExit => null;

    private void OnEnterFunc(EUIState fromState, IUIStateContext? context, object? payload = null)
    {
        UIVo? vo = context?.UIVo;
        if (vo == null) return;

        bool hasCache = vo.Runtime.UI != null && GodotObject.IsInstanceValid(vo.Runtime.UI);
        if (hasCache && vo.Runtime.UI!.IsQueuedForDeletion())
        {
            vo.Runtime.UI = null;
            hasCache = false;
        }

        switch (fromState)
        {
            case EUIState.Cache:
                if (hasCache)
                {
                    vo.Runtime.AddToNode();
                    vo.StateMachine.TransitionTo(EUIState.PreLoad, context, OpenTransitionData.Reopen);
                    return;
                }
                break;
            default:
                if (hasCache)
                {
                    vo.Runtime.AddToNode();
                    OpenTransitionData reopenTag = fromState == EUIState.Close
                        ? OpenTransitionData.CloseOpen
                        : OpenTransitionData.Reopen;
                    vo.StateMachine.TransitionTo(EUIState.PreLoad, context, reopenTag);
                    return;
                }
                break;
        }

        _ = LoadAsync(context, fromState);
    }

    private static async Task LoadAsync(IUIStateContext? context, EUIState fromState)
    {
        UIVo? vo = context?.UIVo;
        if (vo == null) return;

        UIManager? manager = context?.UIManager;
        if (manager == null) return;

        int loadFlag = vo.Load.IncrementLoadFlag();

        List<string> paths = [manager.GetPath(vo.Id)];
        vo.Load.HasMaskLoaded = false;

        if (vo.OpenOpt.Mask != null && !string.IsNullOrEmpty(vo.OpenOpt.Mask.Runtime))
        {
            string? maskPath = manager.GetPath(vo.OpenOpt.Mask.Runtime);
            if (!string.IsNullOrEmpty(maskPath))
            {
                vo.Load.HasMaskLoaded = true;
                paths.Add(maskPath);
            }
        }

        if (vo.OpenOpt.PreLoadResList != null)
        {
            paths.AddRange(vo.OpenOpt.PreLoadResList(vo));
        }

        vo.Lifecycle.LoadTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        try
        {
            PackedScene?[] scenes = await UIResourceLoader.LoadScenesAsync(paths, vo.Load.LoadToken.Token);
            if (loadFlag != vo.Load.LoadFlag || vo.StateMachine.CurrentState != EUIState.Load)
            {
                return;
            }

            if (scenes.Length == 0 || scenes[0] == null)
            {
                AppLog.Error($"UI 管理器: 加载UI<{vo.Id}> 失败，资源<{string.Join(",", paths)}> 不存在。", "UI");
                vo.StateMachine.TransitionTo(EUIState.Destroy, context);
                context?.OpenNext();
                return;
            }

            Node? node = scenes[0]!.Instantiate();
            if (node is not BaseWin win)
            {
                AppLog.Error($"UI 管理器: 加载UI<{vo.Id}> 失败，根节点不是BaseWin。", "UI");
                node?.QueueFree();
                vo.StateMachine.TransitionTo(EUIState.Destroy, context);
                context?.OpenNext();
                return;
            }

            vo.Runtime.UI = win;
            win.UIVo = vo;

            if (vo.Load.HasMaskLoaded && scenes.Length > 1 && scenes[1] != null)
            {
                Node? maskNode = scenes[1]!.Instantiate();
                if (maskNode is BaseMask mask)
                {
                    vo.Runtime.Mask = mask;
                }
                else
                {
                    maskNode?.QueueFree();
                }
            }

            OpenTransitionData transitionData = fromState == EUIState.Close
                ? OpenTransitionData.CloseOpen
                : OpenTransitionData.FirstOpen;
            vo.StateMachine.TransitionTo(EUIState.PreLoad, context, transitionData);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            AppLog.Error($"UI 管理器: 加载UI<{vo.Id}> 失败，{e.Message}", "UI");
            vo.StateMachine.TransitionTo(EUIState.Destroy, context);
            context?.OpenNext();
        }
    }
}