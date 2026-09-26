using KemoCard.Frame.Condition;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Condition;

/// <summary>
/// 战斗条件上下文的实现：把 <see cref="CombatSimulation"/> 在本回合的出牌登记翻译成
/// 条件处理器能读的查询，并携带**条件主体**（身份类条件用）。
/// </summary>
/// <remarks>
/// 三个消费方：
/// <list type="bullet">
/// <item>效果 <c>conditions</c>：主体缺省 = 来源角色（<c>CombatEffectExecutor</c>）；</item>
/// <item>钩子/技能动作的 <c>targetFilter.condition</c>：逐候选把主体设为该候选（<c>CombatTargetSelector</c>）；</item>
/// <item>buff 持有者条件：主体 = 持有者（<c>BuffContainer</c> 走自己的主体上下文）。</item>
/// </list>
/// </remarks>
internal sealed class CombatCondContext : ICombatCondContext
{
    private readonly CombatSimulation _simulation;

    public CombatCondContext(
        CombatSimulation simulation,
        int sourceCharacterIndex,
        int subjectElementFlags = 0,
        int subjectRaceFlags = 0)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        _simulation = simulation;
        SourceCharacterIndex = sourceCharacterIndex;
        SubjectElementFlags = subjectElementFlags;
        SubjectRaceFlags = subjectRaceFlags;
    }

    public int TurnNumber => _simulation.TurnNumber;

    public int TurnsIntoWave => _simulation.TurnsIntoWave;

    public int SourceCharacterIndex { get; }

    public int SubjectElementFlags { get; }

    public int SubjectRaceFlags { get; }

    /// <summary>结算期间记录尚未被取走（<c>TakePlayedThisTurn</c> 在回合结束产球时才清空），此处只读。</summary>
    public int CountCardsPlayedThisTurn(int characterIndex, int elementFlags) =>
        _simulation.CountCardsPlayedThisTurn(characterIndex, elementFlags);

    /// <summary>本回合连携定档的参与人数：<paramref name="elementFlags"/> 为 0 时取当前结算卡牌的属性。</summary>
    public int CountChainParticipants(int elementFlags) =>
        _simulation.CountChainParticipants(elementFlags);

    public int CountPartyIdentityMatches(int elementFlags, int raceFlags, bool matchAll) =>
        _simulation.PlayerTeam.CountMatchingMembers(elementFlags, raceFlags, matchAll);
}
