using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Gas;

public interface IGameplayEffectHookDispatcher
{
    void DispatchApply(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions) { }
    void DispatchStackChanged(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions) { }
    void DispatchTurnStart(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions);

    void DispatchTurnEnd(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions);

    void DispatchRemove(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions);
}