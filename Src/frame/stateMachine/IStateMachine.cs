namespace KemoCard.Frame.StateMachine;

public interface IStateMachine<TState, TContext> where TState : notnull, Enum
{
    TState CurrentState { get; }
    void Enter(TState from, TContext? context = default, object? data = null);
    void ClearState();
    void Configure(TState state, Action<TState, TContext?, object?>? onEnter, Action<TState, TContext?>? onExit = null);
    void SetInitialState(TState state);
    void TransitionTo(TState targetState, TContext? context = default, object? data = null);
    IEnumerable<IStateHandler<TState, TContext>> GetConfigHandlers();
}

public interface IStateHandler<TState, TContext> where TState : notnull, Enum
{
    TState State { get; }
    Action<TState, TContext, object?>? OnEnter { get; }
    Action<TState, TContext>? OnExit { get; }
}