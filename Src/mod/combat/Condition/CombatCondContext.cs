using KemoCard.Frame.Condition;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Condition;

/// <summary>
/// 战斗条件上下文的实现：把 <see cref="CombatSimulation"/> 在本回合的出牌登记翻译成
/// 条件处理器能读的查询。由 <c>CombatEffectExecutor</c> 在求值效果 <c>conditions</c> 时构造。
/// </summary>
internal sealed class CombatCondContext : ICombatCondContext
{
    private readonly CombatSimulation _simulation;

    public CombatCondContext(CombatSimulation simulation, int sourceCharacterIndex)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        _simulation = simulation;
        SourceCharacterIndex = sourceCharacterIndex;
    }

    public int TurnNumber => _simulation.TurnNumber;

    public int TurnsIntoWave => _simulation.TurnsIntoWave;

    public int SourceCharacterIndex { get; }

    /// <summary>结算期间记录尚未被取走（<c>TakePlayedThisTurn</c> 在回合结束产球时才清空），此处只读。</summary>
    public int CountCardsPlayedThisTurn(int characterIndex, int elementFlags) =>
        _simulation.CountCardsPlayedThisTurn(characterIndex, elementFlags);

    /// <summary>本回合连携定档的参与人数；<paramref name="elementFlags"/> 为 0 时取当前结算卡牌的属性。</summary>
    public int CountChainParticipants(int elementFlags) =>
        _simulation.CountChainParticipants(elementFlags);
}