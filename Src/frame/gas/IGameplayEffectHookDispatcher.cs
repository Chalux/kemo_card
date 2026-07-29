using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Gas;

public interface IGameplayEffectHookDispatcher
{
    void DispatchTurnStart(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions);

    void DispatchTurnEnd(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions);

    void DispatchRemove(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions);
}