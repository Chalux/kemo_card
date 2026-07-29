using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>?? �3.2 / �4.2 / �4.4 / �6.2??????????</summary>
[TestFixture]
public sealed class PlayerPhasePipelineTests
{
    #region ?????????

    [Test]
    public void First_player_phase_does_not_regen_current_energy_but_still_refills_available()
    {
        using var sim = BuildSimulation(initialEnergy: 2, maxEnergy: 5);

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: true);

        var character = sim.PlayerTeam.Characters[0];
        Assert.That(character.CurrentEnergy, Is.EqualTo(2), "???????? +1");
        Assert.That(character.AvailableEnergy, Is.EqualTo(2), "???????????");
    }

    [Test]
    public void Later_player_phases_regen_current_energy_before_refilling()
    {
        using var sim = BuildSimulation(initialEnergy: 2, maxEnergy: 5);

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);

        var character = sim.PlayerTeam.Characters[0];
        Assert.That(character.CurrentEnergy, Is.EqualTo(3));
        Assert.That(character.AvailableEnergy, Is.EqualTo(3));
    }

    [Test]
    public void Refill_overwrites_leftover_available_energy()
    {
        using var sim = BuildSimulation(initialEnergy: 2, maxEnergy: 5);
        var character = sim.PlayerTeam.Characters[0];
        character.GainAvailableEnergy(9);

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: true);

        Assert.That(character.AvailableEnergy, Is.EqualTo(2), "??????");
    }

    [Test]
    public void Skill_counter_ticks_on_every_phase_including_the_first()
    {
        using var sim = BuildSimulation(initialEnergy: 0, maxEnergy: 3, skillCounterCap: 2);
        var character = sim.PlayerTeam.Characters[0];

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: true);
        Assert.That(character.SkillCounter, Is.EqualTo(1), "??? S ? +1");

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);
        Assert.That(character.SkillCounter, Is.EqualTo(2));

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);
        Assert.That(character.SkillCounter, Is.EqualTo(2), "? Cap ????");
    }

    #endregion

    #region ????

    [Test]
    public void First_player_phase_skips_the_draw_formula()
    {
        using var sim = BuildSimulation(deckSize: 5);

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: true);

        Assert.That(HandCount(sim, 0), Is.Zero, "?????????");
    }

    [Test]
    public void Later_phases_draw_one_card_without_modifiers()
    {
        using var sim = BuildSimulation(deckSize: 5);

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);

        Assert.That(HandCount(sim, 0), Is.EqualTo(1));
    }

    [Test]
    public void Draw_formula_takes_largest_bonus_and_largest_penalty_only()
    {
        var character = CreateCharacter(deckSize: 10);

        character.AddDrawModifier(2);
        character.AddDrawModifier(1);
        character.AddDrawModifier(-1);
        character.AddDrawModifier(-3);

        // max(0, 1 + 2 - 3) = 0
        Assert.That(character.ComputeDrawCount(), Is.Zero);
    }

    [Test]
    public void Draw_formula_stacks_only_the_single_largest_bonus()
    {
        var character = CreateCharacter(deckSize: 10);

        character.AddDrawModifier(2);
        character.AddDrawModifier(2);

        Assert.That(character.ComputeDrawCount(), Is.EqualTo(3), "??????");
    }

    [Test]
    public void Draw_modifiers_are_consumed_by_the_pipeline_draw_step()
    {
        using var sim = BuildSimulation(deckSize: 10);
        var character = sim.PlayerTeam.Characters[0];
        character.AddDrawModifier(2);

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);
        Assert.That(HandCount(sim, 0), Is.EqualTo(3));

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);
        Assert.That(HandCount(sim, 0), Is.EqualTo(4), "???????????????? 1");
    }

    [Test]
    public void Draw_stops_when_hand_is_full()
    {
        var character = CreateCharacter(deckSize: 10);

        var drawn = character.DrawWithReshuffle(9, new HostRng(1, "combat.draw"));

        Assert.That(drawn, Is.EqualTo(CombatConstants.HandSlotCount));
        Assert.That(character.HandSlots.Count(slot => !slot.IsEmpty), Is.EqualTo(5));
    }

    #endregion

    #region ????

    [Test]
    public void Empty_draw_pile_reshuffles_graveyard_and_keeps_drawing()
    {
        var character = CreateCharacter(deckSize: 3);
        var rng = new HostRng(7, "combat.draw");
        character.DrawWithReshuffle(3, rng);
        DumpHandToGraveyard(character, 3);
        Assert.That(character.DrawPile, Is.Empty);
        Assert.That(character.Graveyard, Has.Count.EqualTo(3));

        character.ResetPhaseShuffleBudget();
        var drawn = character.DrawWithReshuffle(2, rng);

        Assert.That(drawn, Is.EqualTo(2));
        Assert.That(character.Graveyard, Is.Empty, "?????????");
        Assert.That(character.DrawPile, Has.Count.EqualTo(1));
    }

    [Test]
    public void Only_one_reshuffle_is_allowed_per_phase()
    {
        var character = CreateCharacter(deckSize: 2);
        var rng = new HostRng(7, "combat.draw");
        character.ResetPhaseShuffleBudget();

        // ? 2 ? ? ?? ? ?? ? ??????? 2 ? ? ?? ? ??????????
        character.DrawWithReshuffle(2, rng);
        DumpHandToGraveyard(character, 2);
        Assert.That(character.DrawWithReshuffle(2, rng), Is.EqualTo(2), "??????????");
        DumpHandToGraveyard(character, 2);

        Assert.That(character.DrawWithReshuffle(2, rng), Is.Zero, "???????????????");
        Assert.That(character.Graveyard, Has.Count.EqualTo(2));
    }

    [Test]
    public void Reshuffle_budget_resets_each_phase()
    {
        var character = CreateCharacter(deckSize: 2);
        var rng = new HostRng(7, "combat.draw");
        character.ResetPhaseShuffleBudget();
        character.DrawWithReshuffle(2, rng);
        DumpHandToGraveyard(character, 2);
        character.DrawWithReshuffle(2, rng);
        DumpHandToGraveyard(character, 2);
        Assert.That(character.DrawWithReshuffle(1, rng), Is.Zero);

        character.ResetPhaseShuffleBudget();

        Assert.That(character.DrawWithReshuffle(1, rng), Is.EqualTo(1), "?????????");
    }

    [Test]
    public void Empty_draw_pile_and_empty_graveyard_draws_nothing()
    {
        var character = CreateCharacter(deckSize: 0);
        character.ResetPhaseShuffleBudget();

        Assert.That(character.DrawWithReshuffle(3, new HostRng(1, "combat.draw")), Is.Zero);
    }

    [Test]
    public void Reshuffle_order_is_reproducible_for_a_fixed_seed()
    {
        var first = DrawOrderAfterReshuffle(seed: 4242);
        var second = DrawOrderAfterReshuffle(seed: 4242);
        var different = DrawOrderAfterReshuffle(seed: 99);

        Assert.That(first, Is.EqualTo(second), "????????");
        Assert.That(first, Is.Not.EqualTo(different));
    }

    private static string[] DrawOrderAfterReshuffle(int seed)
    {
        var character = CreateCharacter(deckSize: 6);
        var rng = new HostRng(seed, "combat.draw");
        character.ResetPhaseShuffleBudget();
        character.DrawWithReshuffle(5, rng);
        DumpHandToGraveyard(character, 5);
        character.DrawWithReshuffle(5, rng);
        return [.. character.HandSlots.Where(s => !s.IsEmpty).Select(s => s.RuntimeInstanceId!)];
    }

    #endregion

    #region ????

    [Test]
    public void Pipeline_resets_shuffle_budget_at_phase_start()
    {
        using var sim = BuildSimulation(deckSize: 2);
        var character = sim.PlayerTeam.Characters[0];
        var rng = new HostRng(1, "combat.draw");
        character.ResetPhaseShuffleBudget();
        character.DrawWithReshuffle(2, rng);
        DumpHandToGraveyard(character, 2);
        character.DrawWithReshuffle(2, rng);
        DumpHandToGraveyard(character, 2);

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);

        Assert.That(HandCount(sim, 0), Is.EqualTo(1), "?????????????????");
    }

    [Test]
    public void Pipeline_runs_for_every_character()
    {
        using var sim = BuildSimulation(initialEnergy: 1, maxEnergy: 5, deckSize: 5, characterCount: 4);

        PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);

        for (var index = 0; index < 4; index++)
        {
            Assert.That(sim.PlayerTeam.Characters[index].AvailableEnergy, Is.EqualTo(2), $"?? {index}");
            Assert.That(HandCount(sim, index), Is.EqualTo(1), $"?? {index}");
        }
    }

    #endregion

    private static void DumpHandToGraveyard(CharacterBattleInstance character, int count)
    {
        var dumped = 0;
        foreach (var slot in character.HandSlots)
        {
            if (dumped >= count || slot.IsEmpty) continue;
            character.MoveHandCardToGraveyard(slot.RuntimeInstanceId!);
            dumped++;
        }
    }

    private static int HandCount(Mod.Combat.Runtime.CombatSimulation sim, int characterIndex) =>
        sim.PlayerTeam.Characters[characterIndex].HandSlots.Count(slot => !slot.IsEmpty);

    private static CharacterBattleInstance CreateCharacter(
        int deckSize,
        int initialEnergy = 0,
        int maxEnergy = 3,
        int skillCounterCap = 0)
    {
        return CharacterBattleInstance.CreateForTests(
            "hero",
            new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.InitialEnergy] = initialEnergy,
                [AttributeIds.MaxEnergy] = maxEnergy,
            },
            Enumerable.Range(0, deckSize).Select(i => new CardRuntimeEntry("test.card", $"rt-{i}")),
            skillCounterCap);
    }

    private static Mod.Combat.Runtime.CombatSimulation BuildSimulation(
        int initialEnergy = 0,
        int maxEnergy = 3,
        int deckSize = 5,
        int skillCounterCap = 0,
        int characterCount = 1)
    {
        var characters = Enumerable.Range(0, characterCount)
            .Select(_ => CreateCharacter(deckSize, initialEnergy, maxEnergy, skillCounterCap))
            .ToArray();
        return new Mod.Combat.Runtime.CombatSimulation(
            new Mod.Combat.Runtime.PlayerTeamState(characters, sharedMaxHp: 40),
            new Mod.Combat.Runtime.EnemyTeamState([new Mod.Combat.Runtime.EnemyUnit("e0", "slime", maxHp: 100)]),
            new Mod.Combat.Rules.CombatRuleEngine([]),
            CombatTestHelper.CreateFullRegistry(),
            initialPhase: ECombatPhase.Player);
    }
}