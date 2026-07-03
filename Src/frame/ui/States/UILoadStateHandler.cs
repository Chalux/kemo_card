using System.Linq;
using Godot;
using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.Util;

namespace KemoCard.Frame.UI.States;

public sealed class UILoadStateHandler : IStateHandler<EUIState, IUIStateContext>
{
    public EUIState State => EUIState.Load;

    public Action<EUIState, IUIStateContext, object?>? OnEnter => OnEnterFunc;
    public Action<EUIState, IUIStateContext>? OnExit => null;

    private void OnEnterFunc(EUIState fromState, IUIStateContext context, object? payload = null)
    {
        UIVo vo = context.UIVo;
        UIManager manager = context.UIManager;

        bool hasCache = vo.UI != null && GodotObject.IsInstanceValid(vo.UI);
        if (hasCache && vo.UI!.IsQueuedForDeletion())
        {
            vo.UI = null;
            hasCache = false;
        }

        switch (fromState)
        {
            case EUIState.Cache:
                if (hasCache)
                {
                    vo.AddToNode();
                    vo.StateMachine.TransitionTo(EUIState.PreLoad, context);
                    return;
                }
                break;
            default:
                if (hasCache)
                {
                    vo.AddToNode();
                    string reopenTag = fromState == EUIState.Close ? "closeOpen" : "reopen";
                    vo.StateMachine.TransitionTo(EUIState.Load, context, reopenTag);
                    return;
                }
                break;
        }

        _ = LoadAsync(context, fromState);
    }

    private static async Task LoadAsync(IUIStateContext context, EUIState fromState)
    {
        UIVo vo = context.UIVo;
        UIManager manager = context.UIManager;
        int loadFlag = ++vo.LoadFlag;

        List<string> paths = [manager.GetPath(vo.Id)];
        vo.HasMaskLoaded = false;

        if (vo.OpenOpt.Mask != null && !string.IsNullOrEmpty(vo.OpenOpt.Mask.Runtime))
        {
            string? maskPath = manager.GetPath(vo.OpenOpt.Mask.Runtime);
            if (!string.IsNullOrEmpty(maskPath))
            {
                vo.HasMaskLoaded = true;
                paths.Add(maskPath);
            }
        }

        if (vo.OpenOpt.PreLoadResList != null)
        {
            paths.AddRange(vo.OpenOpt.PreLoadResList(vo));
        }

        vo.LoadTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        try
        {
            PackedScene?[] scenes = await UIResourceLoader.LoadScenesAsync(paths, vo.LoadToken.Token);
            if (loadFlag == vo.LoadFlag || vo.StateMachine.CurrentState != EUIState.Load)
            {
                return;
            }

            if (scenes.Length == 0 || scenes[0] == null)
            {
                GD.PushError($"UI 管理器: 加载UI<{vo.Id}> 失败，资源<{string.Join(",", paths)}> 不存在。");
                vo.StateMachine.TransitionTo(EUIState.Destroy);
                context.OpenNext();
                return;
            }

            Node? node = scenes[0]!.Instantiate();
            if (node is not BaseWin win)
            {
                GD.PushError($"UI 管理器: 加载UI<{vo.Id}> 失败，根节点不是BaseWin。");
                node?.QueueFree();
                vo.StateMachine.TransitionTo(EUIState.Destroy);
                context.OpenNext();
                return;
            }

            vo.UI = win;
            win.UIVo = vo;

            if (vo.HasMaskLoaded && scenes.Length > 1 && scenes[1] != null)
            {
                Node? maskNode = scenes[1]!.Instantiate();
                if (maskNode is BaseMask mask)
                {
                    vo.Mask = mask;
                }
                else
                {
                    maskNode?.QueueFree();
                }
            }

            vo.StateMachine.TransitionTo(EUIState.PreLoad, context, fromState == EUIState.Close ? "closeOpen" : null);
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception e)
        {
            GD.PushError($"UI 管理器: 加载UI<{vo.Id}> 失败，{e.Message}");
            vo.StateMachine.TransitionTo(EUIState.Destroy);
            context.OpenNext();
        }
    }
}