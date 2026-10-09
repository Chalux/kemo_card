namespace KemoCard.Mod.Combat.Effects;

/// <summary>贯穿同步钩子、动作链和脚本提案的预算；公开入口的重入不能重置它。</summary>
internal sealed class EffectExecutionBudget
{
    private int _depth;
    private int _steps;
    public int RejectedCount { get; private set; }

    public Scope? TryEnter()
    {
        if (_depth == 0)
            _steps = 0;
        if (_depth >= 32)
        {
            RejectedCount++;
            return null;
        }
        if (!TrySpendStep())
            return null;
        _depth++;
        return new Scope(this);
    }

    /// <summary>循环中的每次命中也消耗预算；同一次结算中的兄弟动作不能重置步数。</summary>
    public bool TrySpendStep()
    {
        if (_depth == 0)
            _steps = 0;
        if (++_steps <= 4096)
            return true;
        RejectedCount++;
        return false;
    }

    internal readonly struct Scope(EffectExecutionBudget owner) : IDisposable
    {
        public void Dispose() => owner._depth--;
    }
}