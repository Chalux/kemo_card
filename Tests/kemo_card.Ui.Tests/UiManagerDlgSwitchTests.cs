using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.UI.States;
using KemoCard.Frame.UI;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 对话框切换相关的状态流转测试（不依赖 Godot 场景树）。
/// 验证泛型 StateMachine 在执行完整 UI 生命周期流转时的行为。
/// </summary>
[TestFixture]
public sealed class UiManagerDlgSwitchTests
{
    [Test]
    public void Different_dialogs_are_queued_without_canceling_existing_dialog()
    {
        var coordinator = new UIOpenCoordinator(null!);
        var first = Dialog("first");
        var second = Dialog("second");
        coordinator.EnqueueOpen(first);
        coordinator.EnqueueOpen(second);
        Assert.That(coordinator.CurrentOpening, Is.SameAs(first));
        Assert.That(first.Load.LoadToken.IsCancellationRequested, Is.False);
        first.StateMachine.TransitionTo(EUIState.Open);
        coordinator.OpenNext();
        Assert.That(coordinator.CurrentOpening, Is.SameAs(second));
        Assert.That(first.IsOpen, Is.True, "打开新 Dlg 不关闭已有 Dlg");
    }

    [Test]
    public void Repeated_queued_dialog_occupies_only_one_queue_entry()
    {
        var coordinator = new UIOpenCoordinator(null!);
        var first = Dialog("first");
        var second = Dialog("second");
        coordinator.EnqueueOpen(first);
        coordinator.EnqueueOpen(second);
        coordinator.EnqueueOpen(second);
        first.StateMachine.TransitionTo(EUIState.Open);
        coordinator.OpenNext();
        second.StateMachine.TransitionTo(EUIState.Open);
        coordinator.OpenNext();
        Assert.That(coordinator.CurrentOpening, Is.Null);
        Assert.That(first.IsOpen && second.IsOpen, Is.True);
    }

    private static UIVo Dialog(string id)
    {
        var vo = new UIVo(id, EUIType.Dlg, "tests", null, null!, []);
        vo.StateMachine.Configure(EUIState.Load, (_, context, _) => vo.StateMachine.TransitionTo(EUIState.PreLoad, context));
        return vo;
    }

    [Test]
    public void Full_open_lifecycle_sequence_transitions_correctly()
    {
        var sm = new StateMachine<EUIState, IUIStateContext>();
        var steps = new List<EUIState>();

        sm.Configure(EUIState.Wait, null,
            onExit: (target, _) => steps.Add(EUIState.Wait));
        sm.Configure(EUIState.Load,
            onEnter: (from, _, _) => steps.Add(EUIState.Load));
        sm.Configure(EUIState.PreLoad,
            onEnter: (from, _, _) => steps.Add(EUIState.PreLoad));
        sm.Configure(EUIState.Create,
            onEnter: (from, _, _) => steps.Add(EUIState.Create));
        sm.Configure(EUIState.Open,
            onEnter: (from, _, _) => steps.Add(EUIState.Open));

        sm.SetInitialState(EUIState.Wait);
        sm.TransitionTo(EUIState.Load);
        sm.TransitionTo(EUIState.PreLoad);
        sm.TransitionTo(EUIState.Create);
        sm.TransitionTo(EUIState.Open);

        Assert.That(sm.CurrentState, Is.EqualTo(EUIState.Open));
        Assert.That(steps, Is.EqualTo(new[] { EUIState.Wait, EUIState.Load, EUIState.PreLoad, EUIState.Create, EUIState.Open }));
    }

    [Test]
    public void Close_lifecycle_sequence_transitions_correctly()
    {
        var sm = new StateMachine<EUIState, IUIStateContext>();
        var steps = new List<EUIState>();

        sm.Configure(EUIState.Open, null,
            onExit: (target, _) => steps.Add(EUIState.Open));
        sm.Configure(EUIState.Close,
            onEnter: (from, _, _) => steps.Add(EUIState.Close),
            onExit: (target, _) => steps.Add(EUIState.Close));
        sm.Configure(EUIState.CloseDone,
            onEnter: (from, _, _) => steps.Add(EUIState.CloseDone));

        sm.SetInitialState(EUIState.Open);
        sm.TransitionTo(EUIState.Close);
        sm.TransitionTo(EUIState.CloseDone);

        Assert.That(sm.CurrentState, Is.EqualTo(EUIState.CloseDone));
        Assert.That(steps, Is.EqualTo(new[] { EUIState.Open, EUIState.Close, EUIState.Close, EUIState.CloseDone }));
    }
}