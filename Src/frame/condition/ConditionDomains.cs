namespace KemoCard.Frame.Condition;

public static class ConditionDomains
{
    public static ConditionRegistry<IPersistentCondContext> Persistent { get; } = new();
    public static ConditionRegistry<ICombatCondContext> Combat { get; } = new();
}