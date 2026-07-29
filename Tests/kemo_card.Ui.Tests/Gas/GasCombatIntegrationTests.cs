using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;
using KemoCard.Ui.Tests.Combat;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class GasCombatIntegrationTests
{
    [Test]
    public void Full_battle_with_ge_damage_is_deterministic()
    {
        (int SharedHp, int EnemyHp, ECombatPhase Phase) RunOnce()
        {
            var sim = CombatSimulationTestBuilder.FullBattle(seed: 12345);
            sim.TryApply(new ConfirmCharacterCommand(0));
            sim.TryApply(new ConfirmCharacterCommand(1));
            sim.TryApply(new ConfirmCharacterCommand(2));
            sim.TryApply(new ConfirmCharacterCommand(3));
            sim.AdvancePhase();
            return (sim.PlayerTeam.SharedHp, sim.EnemyTeam.Enemies[0].CurrentHp, sim.Phase);
        }

        Assert.That(RunOnce(), Is.EqualTo(RunOnce()));
    }

    [Test]
    public void Weak_ge_increases_damage_taken()
    {
        var source = new CombatTargetRef(ECombatSide.Player, 0);
        var target = new CombatTargetRef(ECombatSide.Enemy, 0);

        var baseline = CreateMinimalSimulation();
        baseline.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto { ActionId = "apply_strike_damage", Params = new Dictionary<string, object> { ["Amount"] = 6f } },
            baseline,
            source,
            [target]);
        var baselineHp = baseline.EnemyTeam.Enemies[0].CurrentHp;

        var weakened = CreateMinimalSimulation();
        weakened.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto { ActionId = "apply_weak" },
            weakened,
            source,
            [target]);
        weakened.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto { ActionId = "apply_strike_damage", Params = new Dictionary<string, object> { ["Amount"] = 6f } },
            weakened,
            source,
            [target]);
        var weakenedHp = weakened.EnemyTeam.Enemies[0].CurrentHp;

        Assert.That(weakenedHp, Is.LessThan(baselineHp));
    }

    private static CombatSimulation CreateMinimalSimulation()
    {
        var attributes = new Dictionary<string, AttributeDefDto>(StringComparer.Ordinal)
        {
            [AttributeIds.Health] = new() { Id = AttributeIds.Health, DefaultBase = 0f, AllowNegative = false },
            [AttributeIds.MaxHealth] = new() { Id = AttributeIds.MaxHealth, DefaultBase = 0f, AllowNegative = false },
            [AttributeIds.PhysicalAttack] = new() { Id = AttributeIds.PhysicalAttack, DefaultBase = 0f, AllowNegative = false },
            [AttributeIds.PhysicalDefense] = new() { Id = AttributeIds.PhysicalDefense, DefaultBase = 0f, AllowNegative = false },
            [AttributeIds.Damage] = new() { Id = AttributeIds.Damage, DefaultBase = 0f, AllowNegative = false },
            [AttributeIds.DamageTakenScale] = new() { Id = AttributeIds.DamageTakenScale, DefaultBase = 0f, AllowNegative = true },
        };
        var gameplayEffects = new Dictionary<string, GameplayEffectDefDto>(StringComparer.Ordinal)
        {
            ["strike_damage"] = new()
            {
                Id = "strike_damage",
                DurationPolicy = EDurationPolicy.Instant,
                Executions = [new ExecutionDefDto { Kind = "Damage", DamageType = "Physical" }],
                Modifiers =
                [
                    new AttributeModifierDefDto
                    {
                        AttributeId = AttributeIds.Damage,
                        Operation = EAttributeModifierOp.Override,
                        Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.SetByCaller, CallerName = "Amount" },
                    },
                ],
            },
            ["weak"] = new()
            {
                Id = "weak",
                DurationPolicy = EDurationPolicy.HasDuration,
                DurationTurns = 2,
                StackingPolicy = EStackingPolicy.AggregateByTarget,
                MaxStacks = 3,
                Modifiers =
                [
                    new AttributeModifierDefDto
                    {
                        AttributeId = AttributeIds.DamageTakenScale,
                        Operation = EAttributeModifierOp.Add,
                        Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = 1f },
                    },
                ],
            },
        };
        var skillActions = new Dictionary<string, SkillActionDto>(StringComparer.Ordinal)
        {
            ["apply_strike_damage"] = new()
            {
                Id = "apply_strike_damage",
                Kind = ESkillActionKind.ApplyGameplayEffect,
                Params = new Dictionary<string, object> { ["gameplayEffectId"] = "strike_damage", ["Amount"] = 6f },
            },
            ["apply_weak"] = new()
            {
                Id = "apply_weak",
                Kind = ESkillActionKind.ApplyGameplayEffect,
                Params = new Dictionary<string, object> { ["gameplayEffectId"] = "weak" },
            },
        };

        var registry = CombatTestHelper.CreateFullRegistry(
            attributes: attributes,
            gameplayEffects: gameplayEffects,
            skillActions: skillActions);

        var player = new PlayerTeamState(
            [
                CharacterBattleInstance.CreateForTests(
                    "player-0",
                    new Dictionary<string, float>(StringComparer.Ordinal)
                    {
                        [AttributeIds.MaxHealth] = 20f,
                        [AttributeIds.Health] = 20f,
                        [AttributeIds.PhysicalAttack] = 2f,
                    }),
            ],
            sharedMaxHp: 20);
        var enemy = new EnemyUnit(
            "enemy-0",
            "slime",
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.MaxHealth] = 20f,
                [AttributeIds.Health] = 20f,
                [AttributeIds.PhysicalDefense] = 1f,
                [AttributeIds.DamageTakenScale] = 0f,
            });
        return new CombatSimulation(
            player,
            new EnemyTeamState([enemy]),
            new CombatRuleEngine([]),
            registry);
    }
}