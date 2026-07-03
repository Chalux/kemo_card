namespace KemoCard.Frame.StateMachine;

/// <summary>
/// 泛型状态机，管理 TState 的状态流转与 TContext 上下文交互。
/// 通过 Configure 注册各状态的进入/退出回调，通过 TransitionTo 执行状态切换。
/// </summary>
public sealed class StateMachine<TState, TContext> : IStateMachine<TState, TContext>
    where TState : notnull, Enum
    where TContext : notnull
{
    private TState _currentState = default!;
    private readonly Dictionary<TState, StateConfig> _configs = [];

    public TState CurrentState => _currentState;

    /// <summary>
    /// 配置指定状态的进入和退出回调
    /// </summary>
    /// <param name="state">目标状态</param>
    /// <param name="onEnter">进入该状态时触发，参数：(上下文, 来源状态, 附加数据)</param>
    /// <param name="onExit">离开该状态时触发，参数：(上下文, 目标状态)</param>
    public void Configure(TState state, Action<TState, TContext?, object?>? onEnter, Action<TState, TContext?>? onExit = null)
    {
        _configs[state] = new StateConfig(state, onEnter, onExit);
    }

    /// <summary>
    /// 设置初始状态（不触发回调）
    /// </summary>
    public void SetInitialState(TState state)
    {
        _currentState = state;
    }

    /// <summary>
    /// 执行状态切换：先退出当前状态，再进入目标状态
    /// </summary>
    /// <param name="context">状态上下文</param>
    /// <param name="targetState">目标状态</param>
    /// <param name="data">传递给目标状态进入回调的附加数据</param>
    public void TransitionTo(TState targetState, TContext? context = default, object? data = null)
    {
        var prevState = _currentState;

        if (_configs.TryGetValue(prevState, out var prevConfig) && prevConfig.OnExit is not null)
        {
            prevConfig.OnExit(targetState, context);
        }

        _currentState = targetState;

        if (_configs.TryGetValue(targetState, out var targetConfig) && targetConfig.OnEnter is not null)
        {
            targetConfig.OnEnter(prevState, context, data);
        }
    }

    /// <summary>
    /// 触发当前状态的进入回调，一般用于首次启动或重新进入已有状态
    /// </summary>
    public void Enter(TState from, TContext? context = default, object? data = null)
    {
        if (_configs.TryGetValue(_currentState, out var config) && config.OnEnter is not null)
        {
            config.OnEnter(from, context, data);
        }
    }

    private sealed class StateConfig(TState state, Action<TState, TContext?, object?>? onEnter, Action<TState, TContext?>? onExit) : IStateHandler<TState, TContext>
    {
        public TState State => state;
        public Action<TState, TContext?, object?>? OnEnter => onEnter;
        public Action<TState, TContext?>? OnExit => onExit;
    }

    public void ClearState()
    {
        _currentState = default!;
        _configs.Clear();
    }

    public IEnumerable<IStateHandler<TState, TContext>> GetConfigHandlers()
    {
        return _configs.Values;
    }
}