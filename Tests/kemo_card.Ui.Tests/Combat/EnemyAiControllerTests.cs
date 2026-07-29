using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.Ai;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class EnemyAiControllerTests
{
    [Test]
    public void ChooseSkill_is_deterministic_with_same_seed()
    {
        var registry = CreateSlimeRegistry();
        var rng1 = new HostRng(1, "combat.ai");
        var rng2 = new HostRng(1, "combat.ai");
        var ai1 = new EnemyAiController(registry, scriptInvoker: null, modId: "test.mod");
        var ai2 = new EnemyAiController(registry, scriptInvoker: null, modId: "test.mod");
        var enemy = new EnemyUnit("e", "slime", maxHp: 10);

        Assert.That(ai1.ChooseSkill(enemy, rng1), Is.EqualTo(ai2.ChooseSkill(enemy, rng2)));
    }

    [Test]
    public void ChooseSkill_returns_null_when_enemy_has_no_legal_skills()
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            enemies: new Dictionary<string, EnemyDto>
            {
                ["empty"] = new()
                {
                    Id = "empty",
                    MaxHp = 10,
                    SkillRefs = [new SkillRefDto { SkillId = "missing.skill" }],
                },
            });
        var ai = new EnemyAiController(registry, scriptInvoker: null, modId: "test.mod");
        var enemy = new EnemyUnit("e", "empty", maxHp: 10);
        var rng = new HostRng(1, "combat.ai");

        Assert.That(ai.ChooseSkill(enemy, rng), Is.Null);
    }

    private static GameDefinitionRegistry CreateSlimeRegistry()
    {
        var skills = new Dictionary<string, SkillDto>
        {
            ["slime.bite"] = new()
            {
                Id = "slime.bite",
                DisplayNameId = "slime.bite",
                DescId = "slime.bite.desc",
            },
            ["slime.splash"] = new()
            {
                Id = "slime.splash",
                DisplayNameId = "slime.splash",
                DescId = "slime.splash.desc",
            },
        };
        var enemies = new Dictionary<string, EnemyDto>
        {
            ["slime"] = new()
            {
                Id = "slime",
                MaxHp = 10,
                SkillRefs =
                [
                    new SkillRefDto { SkillId = "slime.bite" },
                    new SkillRefDto { SkillId = "slime.splash" },
                ],
            },
        };

        return CombatTestHelper.CreateFullRegistry(skills: skills, enemies: enemies);
    }
}