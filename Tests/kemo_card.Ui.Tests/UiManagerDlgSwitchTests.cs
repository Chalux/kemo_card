using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.UI.States;
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

    [Test]
    public void Reopen_after_close_completes_full_cycle()
    {
        var sm = new StateMachine<EUIState, IUIStateContext>();

        sm.SetInitialState(EUIState.CloseDone);
        sm.TransitionTo(EUIState.Wait);
        sm.TransitionTo(EUIState.Load);
        sm.TransitionTo(EUIState.Create);
        sm.TransitionTo(EUIState.Open);

        Assert.That(sm.CurrentState, Is.EqualTo(EUIState.Open));
    }
}
