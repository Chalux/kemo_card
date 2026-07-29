using KemoCard.Mod.Combat.Rules.Builtin;

namespace KemoCard.Mod.Combat.Rules;

public sealed class CombatRuleCatalog
{
    private readonly Dictionary<string, Func<ICombatRule>> _factories = new(StringComparer.Ordinal);

    public void Register(string ruleId, Func<ICombatRule> factory) => _factories[ruleId] = factory;

    public ICombatRule? TryCreate(string ruleId) =>
        _factories.TryGetValue(ruleId, out var factory) ? factory() : null;

    public static CombatRuleCatalog CreateDefault()
    {
        var catalog = new CombatRuleCatalog();
        catalog.Register(SharedHpDefeatRule.RuleId, () => new SharedHpDefeatRule());
        catalog.Register(AllEnemiesDefeatedVictoryRule.RuleId, () => new AllEnemiesDefeatedVictoryRule());
        return catalog;
    }
}