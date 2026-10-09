using Godot;
using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.States;

/// <summary>
/// 销毁 UI 状态处理器: 清理资源
/// </summary>
public sealed class UIDestroyStateHandler : IStateHandler<EUIState, IUIStateContext>
{
    public EUIState State => EUIState.Destroy;

    public Action<EUIState, IUIStateContext?, object?>? OnEnter => OnEnterAction;
    public Action<EUIState, IUIStateContext?>? OnExit => null;

    public static void OnEnterAction(EUIState state, IUIStateContext? context, object? data)
    {
        if (context == null) return;
        UIVo vo = context.UIVo;
        UIManager manager = context.UIManager;

        var task = vo.OpenTaskSource;
        var onFail = vo.OpenOpt.OnFail;
        // 先解除寻址，清理 hook 重入 OpenAsync 时会得到新 VO。
        manager.VoRegistry.Remove(vo.Id);
        manager.NavStack.Remove(vo.Id);
        manager.OpenCoordinator.ResetCurrentOpening(vo);
        manager.OpenCoordinator.RemoveFromQueue(vo);

        vo.Load.Cancel();
        vo.Anim.ClearAnim();
        vo.Anim.ClearMaskAnim();

        vo.Runtime.HideBool.Destory();
        var mask = vo.Runtime.Mask;
        var ui = vo.Runtime.UI;
        try
        {
            vo.Runtime.RemoveFromNode();
            if (GodotObject.IsInstanceValid(mask))
                UIManager.InvokeCallback(() => ((IUILifecycleInvoker)mask!).InvokeMaskUIDestroy(), vo.Id, "遮罩销毁回调");
        }
        finally
        {
            vo.Runtime.Mask = null;
            vo.Runtime.UI = null;
            if (GodotObject.IsInstanceValid(mask) && !mask!.IsQueuedForDeletion()) mask.QueueFree();
            if (GodotObject.IsInstanceValid(ui) && !ui!.IsQueuedForDeletion()) ui.QueueFree();
            try
            {
                if (task != null) UIManager.InvokeCallback(onFail, vo.Id, "打开失败回调");
            }
            finally { vo.CompleteOpen(null, task); }
            manager.LayerManager.UpdateLayers();
        }
    }
}