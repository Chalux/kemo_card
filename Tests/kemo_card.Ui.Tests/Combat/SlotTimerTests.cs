using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class SlotTimerTests
{
    #region 倒计时与扩散

    [Test]
    public void Enemy_can_attach_a_timer_to_a_target_character_including_an_empty_slot()
    {
        using var sim = KagiContentTests.Build();
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = "attach_slot_timer", Params = new() { ["slotIndex"] = 2, ["timerTurns"] = 2, ["amount"] = 40 } },
            sim, KagiContentTests.Enemy(), [KagiContentTests.Player(1)]);
        Assert.That(Timer(sim, 2, 1)?.RemainingTurns, Is.EqualTo(2));
        Assert.That(Timer(sim, 2), Is.Null);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(Timer(sim, 2, 1)?.RemainingTurns, Is.EqualTo(1));
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(1000));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(960));
        Assert.That(Timer(sim, 2, 1), Is.Null);
        Assert.That(Timer(sim, 1, 1)?.RemainingTurns, Is.EqualTo(2));
        Assert.That(Timer(sim, 3, 1)?.RemainingTurns, Is.EqualTo(2));
        Assert.That(Timer(sim, 1, 1)?.Params!["amount"].ToString(), Is.EqualTo("40"));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(Timer(sim, 1, 1)?.RemainingTurns, Is.EqualTo(1));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(880), "第二批两槽各40伤害");
        Assert.That(Timer(sim, 0, 1)?.RemainingTurns, Is.EqualTo(2));
        Assert.That(Timer(sim, 2, 1)?.RemainingTurns, Is.EqualTo(2));
        Assert.That(Timer(sim, 4, 1)?.RemainingTurns, Is.EqualTo(2));
    }

    [TestCase(0, 1)]
    [TestCase(4, 3)]
    public void Edge_timer_spreads_only_to_the_existing_neighbor(int slot, int neighbor)
    {
        using var sim = KagiContentTests.Build();
        Attach(sim, slot, 1, 25);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(975));
        Assert.That(Timer(sim, neighbor)?.RemainingTurns, Is.EqualTo(1));
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots.Count(s => s.Buffs.HasTag(BuiltinBuffTags.SlotTimer)), Is.EqualTo(1));
    }

    [Test]
    public void Existing_timer_keeps_its_damage_remaining_time_and_initial_duration_when_a_wave_spreads()
    {
        using var sim = KagiContentTests.Build();
        Attach(sim, 1, 4, 7);
        var original = Timer(sim, 1);
        Attach(sim, 2, 1, 30);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(Timer(sim, 1), Is.SameAs(original));
        Assert.That(original!.RemainingTurns, Is.EqualTo(3));
        Assert.That(original.FullDuration, Is.EqualTo(4));
        Assert.That(original.Params!["amount"], Is.EqualTo(7));
        Attach(sim, 1, 10, 100);
        Assert.That(Timer(sim, 1), Is.SameAs(original));
        Assert.That(original.RemainingTurns, Is.EqualTo(3));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(original.RemainingTurns, Is.EqualTo(2));
        Assert.That(Timer(sim, 2)?.RemainingTurns, Is.EqualTo(1));
    }

    [Test]
    public void Simultaneous_expirations_all_fire_once_and_new_timers_do_not_tick_in_the_same_turn()
    {
        using var sim = KagiContentTests.Build();
        Attach(sim, 0, 1, 10);
        Attach(sim, 1, 1, 20);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(970));
        Assert.That(new[] { Timer(sim, 0)!.RemainingTurns, Timer(sim, 1)!.RemainingTurns, Timer(sim, 2)!.RemainingTurns }, Is.EqualTo(new[] { 1, 1, 1 }));
    }

    [Test]
    public void Dispelling_a_timer_does_not_detonate_or_spread_it()
    {
        using var sim = KagiContentTests.Build();
        Attach(sim, 2, 1, 100);
        sim.PlayerTeam.Characters[0].HandSlots[2].Buffs.Remove(Timer(sim, 2)!);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(1000));
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots.All(s => !s.Buffs.HasTag(BuiltinBuffTags.SlotTimer)), Is.True);
    }

    #endregion

    #region 免疫、减伤与开战

    [Test]
    public void Immunity_blocks_enemy_attachment_and_late_immunity_blocks_damage_and_spread()
    {
        using var sim = KagiContentTests.Build();
        Attach(sim, 2, 1, 100);
        KagiContentTests.ApplyPassive(sim, 1);
        sim.EffectExecutor.ExecuteSkillActionRef(new() { ActionId = "attach_slot_timer" }, sim, KagiContentTests.Enemy(), [KagiContentTests.Player()]);
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots.Count(s => s.Buffs.HasTag(BuiltinBuffTags.SlotTimer)), Is.EqualTo(1));
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(1000));
        Assert.That(sim.PlayerTeam.Characters[0].HandSlots.All(s => !s.Buffs.HasTag(BuiltinBuffTags.SlotTimer)), Is.True);
    }

    [Test]
    public void Timer_damage_uses_all_damage_reduction_and_does_not_trigger_attack_counters()
    {
        using var sim = KagiContentTests.Build();
        KagiContentTests.ApplyPassive(sim, 2);
        sim.Buffs.Apply(sim, KagiContentTests.Player(), "zeus_passive_p2");
        var attack = sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.MagicAttack);
        Attach(sim, 2, 1, 100);
        sim.Buffs.FireTurnEnd(sim);
        sim.FlushOnDamagedHits();
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(925));
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(attack));
    }

    [Test]
    public void Full_battle_start_installs_immunity_and_permanent_fifth_slot_charge()
    {
        using var sim = KagiContentTests.Build(start: true);
        sim.RunBattleStart();
        var caster = sim.PlayerTeam.Characters[0];
        Assert.That(caster.Buffs.HasTag(BuiltinBuffTags.TraitImmuneSlotTimer), Is.True);
        Assert.That(caster.HandSlots[4].Buffs.FindByTag(BuiltinBuffTags.SlotCharge)?.ChargeRequired, Is.EqualTo(3));
        Assert.That(caster.SkillCounter, Is.EqualTo(7), "阶层被动6，加首回合基础技能进度1");
        Attach(sim, 0, 1, 100);
        Assert.That(Timer(sim, 0), Is.Null);
    }

    [TestCase(0, 30)]
    [TestCase(-1, 30)]
    [TestCase(3, -10)]
    public void Invalid_timer_payload_is_rejected_by_content_validation(int turns, int amount)
    {
        var defs = BaseGameContent.Load();
        var actions = new Dictionary<string, SkillActionDto>(defs.SkillActions)
        { ["bad_timer"] = new() { Id = "bad_timer", Kind = ESkillActionKind.AttachSlotBuff, Params = new() { ["buffId"] = "slot_timer", ["slotSelection"] = "random", ["timerTurns"] = turns, ["amount"] = amount } } };
        var registry = BaseGameContent.BuildRegistry();
        registry.Rebuild([new ModContentBundle("base.game", defs with { SkillActions = actions })], out var report);
        Assert.That(report.RemovedValidationErrors.Any(e => e.DefinitionId == "bad_timer"), Is.True);
    }

    #endregion

    private static void Attach(CombatSimulation sim, int slot, int turns, int amount) =>
        sim.Buffs.ApplyToSlot(sim, 0, slot, "slot_timer", new Dictionary<string, object> { ["timerTurns"] = turns, ["amount"] = amount });
    private static BuffInstance? Timer(CombatSimulation sim, int slot, int character = 0) =>
        sim.PlayerTeam.Characters[character].HandSlots[slot].Buffs.FindByTag(BuiltinBuffTags.SlotTimer);
}