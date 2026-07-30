using KemoCard.Frame.Condition;

namespace KemoCard.Mod.Combat.Condition;

public static class BuiltinCombatConditions
{
    public static void RegisterAll(ConditionRegistry<ICombatCondContext> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        // v1：有意不注册业务 CondType
    }
}
