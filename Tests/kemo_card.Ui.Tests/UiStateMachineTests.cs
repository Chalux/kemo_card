using KemoCard.Frame.StateMachine;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.UI.States;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class UiStateMachineTests
{
    [Test]
    public void SetInitialState_sets_CurrentState()
    {
        var sm = new StateMachine<EUIState, IUIStateContext>();
        sm.SetInitialState(EUIState.Wait);

        Assert.That(sm.CurrentState, Is.EqualTo(EUIState.Wait));
    }

    [Test]
    public void TransitionTo_changes_state_and_invokes_callbacks()
    {
        var sm = new StateMachine<EUIState, IUIStateContext>();
        var enteredState = EUIState.Wait;
        var exitedState = EUIState.Wait;

        sm.Configure(EUIState.Wait, null,
            onExit: (target, _) => exitedState = target);
        sm.Configure(EUIState.Load,
            onEnter: (from, _, _) => enteredState = from);

        sm.SetInitialState(EUIState.Wait);
        sm.TransitionTo(EUIState.Load);

        Assert.That(sm.CurrentState, Is.EqualTo(EUIState.Load));
        Assert.That(enteredState, Is.EqualTo(EUIState.Wait));
        Assert.That(exitedState, Is.EqualTo(EUIState.Load));
    }

    [Test]
    public void TransitionTo_invokes_onEnter_with_data()
    {
        var sm = new StateMachine<EUIState, IUIStateContext>();
        object? receivedData = null;

        sm.Configure(EUIState.Create,
            onEnter: (_, _, data) => receivedData = data);

        sm.SetInitialState(EUIState.Wait);
        sm.TransitionTo(EUIState.Create, data: "testPayload");

        Assert.That(receivedData, Is.EqualTo("testPayload"));
    }

    [Test]
    public void ClearState_resets_state_machine()
    {
        var sm = new StateMachine<EUIState, IUIStateContext>();
        sm.SetInitialState(EUIState.Wait);
        sm.Configure(EUIState.Wait, onEnter: (_, _, _) => { });
        sm.TransitionTo(EUIState.Load);

        sm.ClearState();

        Assert.That(sm.CurrentState, Is.EqualTo(default(EUIState)));
        Assert.That(sm.GetConfigHandlers(), Is.Empty);
    }

    [Test]
    public void Enter_calls_onEnter_for_current_state()
    {
        var sm = new StateMachine<EUIState, IUIStateContext>();
        var entered = false;

        sm.Configure(EUIState.Wait,
            onEnter: (from, _, _) => entered = true);

        sm.SetInitialState(EUIState.Wait);
        sm.Enter(EUIState.Wait);

        Assert.That(entered, Is.True);
    }
}
