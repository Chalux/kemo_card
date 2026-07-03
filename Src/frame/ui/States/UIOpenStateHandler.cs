using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.States;

public readonly struct UIOpenPayload(UIVo vo)
{
    public UIVo Vo { get; } = vo;
}

/// <summary>
/// 打开 UI 状态处理器
/// </summary>
public sealed class UIOpenStateHandler : IStateHandler<EUIState, IUIStateContext>
{
    public EUIState State => EUIState.Open;

    public Action<EUIState, IUIStateContext, object?>? OnEnter => OnEnterAction;
    public Action<EUIState, IUIStateContext>? OnExit => null;

    public void OnEnterAction(EUIState state, IUIStateContext context, object? data)
    {
        UIVo vo = context.UIVo;
        UIManager manager = context.UIManager;
        BaseWin win = vo.UI!;

        vo.OpenTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (data != null)
        {
            vo.AddToNode();
        }

        win.Payload = vo.Payload;
        vo.OpenOpt.OnOpenBefore?.Invoke(vo);
        vo.OpenOpt.OnOpenBefore = null;

        bool isFirstOpen = data == null;
        if (isFirstOpen)
        {
            vo.Mask?.InternalInitEvent();
            win.InternalInitEvent();

            if (vo.StateMachine.CurrentState != EUIState.Open)
            {
                return;
            }
        }

        if (vo.Mask != null)
        {
            vo.ClearMaskAnim();
            vo.Mask.AnimState = EUIAnimState.Open;
            vo.ClearMaskAnimCallback = vo.Mask.InternalOpenAnim(() =>
            {
                if (vo.Mask!.AnimState == EUIAnimState.Open)
                {
                    vo.Mask.AnimState = EUIAnimState.None;
                }

                vo.Mask.InternalOpenAnimDone();
            });
            vo.Mask.InternalOpen();
        }

        win.InternalOpen();
        vo.Mask?.InternalUIOpen();

        if (vo.StateMachine.CurrentState != EUIState.Open)
        {
            return;
        }

        bool playAnim = vo.OpenOpt.AnimType != EAnimType.None;
        if (playAnim && vo.OpenOpt.AnimType == EAnimType.SkipReOpen && data is "reOpen")
        {
            playAnim = false;
        }

        if (playAnim)
        {
            vo.ClearAnim();
            win.AnimState = EUIAnimState.Open;
            vo.ClearAnimCallback = win.InternalOpenAnim(() =>
            {
                if (win.AnimState == EUIAnimState.Open)
                {
                    win.AnimState = EUIAnimState.None;
                }

                win.InternalOpenAnimDone();
                manager.UpdateLayers();
            });
        }
        else
        {
            manager.UpdateLayers();
        }

        manager.EventDispatcher.Send<UIOpenPayload>(UIEvent.Open, new(vo));
        vo.OpenOpt.OnOpen?.Invoke(vo);
        context.OpenNext();
    }
}