using KemoCard.Frame.Condition;
using KemoCard.Mod.Combat.Condition;
using KemoCard.Mod.Global.Condition;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Condition;

[TestFixture]
public sealed class ConditionDomainsBootstrapTests
{
    private static readonly string[] PersistentCondTypeIds =
    [
        "HasFlag",
        "NotHasFlag",
        "HasAllItems",
        "HasAnyItem",
    ];

    [Test]
    public void RegisterAll_on_fresh_registries_registers_persistent_only()
    {
        var persistent = new ConditionRegistry<IPersistentCondContext>();
        var combat = new ConditionRegistry<ICombatCondContext>();

        BuiltinPersistentConditions.RegisterAll(persistent);
        BuiltinCombatConditions.RegisterAll(combat);

        foreach (var id in PersistentCondTypeIds)
        {
            Assert.That(persistent.Contains(id), Is.True, $"Persistent 应包含 {id}");
            Assert.That(combat.Contains(id), Is.False, $"Combat 不应包含 {id}");
        }
    }

    [Test]
    public void RegisterAll_on_ConditionDomains_after_clear()
    {
        ConditionDomains.Persistent.Clear();
        ConditionDomains.Combat.Clear();

        try
        {
            BuiltinPersistentConditions.RegisterAll(ConditionDomains.Persistent);
            BuiltinCombatConditions.RegisterAll(ConditionDomains.Combat);

            foreach (var id in PersistentCondTypeIds)
            {
                Assert.That(ConditionDomains.Persistent.Contains(id), Is.True);
                Assert.That(ConditionDomains.Combat.Contains(id), Is.False);
            }
        }
        finally
        {
            ConditionDomains.Persistent.Clear();
            ConditionDomains.Combat.Clear();
        }
    }
}
