using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Rules.Builtin;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatEffectExecutorTests
{
    [Test]
    public void Damage_preserves_fractional_amount()
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            effects: new Dictionary<string, EffectDto>
            {
                ["hit"] = new()
                {
                    Id = "hit",
                    Kind = EEffectKind.Damage,
                    Params = new() { ["amount"] = 2.5f },
                },
            });
        var enemy = new EnemyUnit("e0", "slime", maxHp: 20);
        var sim = CombatSimulationTestBuilder.Minimal(enemy, registry, rules: []);
        var executor = new CombatEffectExecutor(registry);
        var source = new CombatTargetRef(ECombatSide.Player, 0);
        var target = new CombatTargetRef(ECombatSide.Enemy, 0);

        executor.ExecuteEffectRef(new EffectRefDto { EffectId = "hit" }, sim, source, [target]);

        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(17.5f));
    }

    [Test]
    public void Damage_reduces_enemy_hp_through_rule_pipeline()
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            effects: new Dictionary<string, EffectDto>
            {
                ["hit"] = new() { Id = "hit", Kind = EEffectKind.Damage, Params = new() { ["amount"] = 5 } },
            });
        var enemy = new EnemyUnit("e0", "slime", maxHp: 20);
        var sim = CombatSimulationTestBuilder.Minimal(enemy, registry, rules: [new SharedHpDefeatRule()]);
        var executor = new CombatEffectExecutor(registry);
        var source = new CombatTargetRef(ECombatSide.Player, 0);
        var target = new CombatTargetRef(ECombatSide.Enemy, 0);

        executor.ExecuteEffectRef(new EffectRefDto { EffectId = "hit" }, sim, source, [target]);

        Assert.That(enemy.CurrentHp, Is.EqualTo(15));
    }

    /// <summary>
    /// 直伤路径（不经 GAS DamageExecution）的缩放补齐：×(1 + 源 DamageDealtScale + 连携)，
    /// 与 DamageExecution 同桶加算（规格 §3），不再只乘连携。
    /// </summary>
    [Test]
    public void Direct_damage_scales_by_source_dealt_scale_and_chain_additively()
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            effects: new Dictionary<string, EffectDto>
            {
                ["hit.scaled"] = new()
                {
                    Id = "hit.scaled",
                    Kind = EEffectKind.Damage,
                    Params = new() { ["amount"] = 10f },
                },
            });
        var enemy = new EnemyUnit("e0", "slime", maxHp: 100);
        var sim = CombatSimulationTestBuilder.Minimal(enemy, registry, rules: []);
        var executor = new CombatEffectExecutor(registry);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.DamageDealtScale, 0.25f);
        sim.SetChainBonus(0.5f);
        var source = new CombatTargetRef(ECombatSide.Player, 0);
        var target = new CombatTargetRef(ECombatSide.Enemy, 0);

        executor.ExecuteEffectRef(new EffectRefDto { EffectId = "hit.scaled" }, sim, source, [target]);

        // 10 × (1 + 0.25 增伤) × (1 + 0.5 连携) = 18.75：增伤与连携分开，连携仍是乘算因子。
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(81.25f).Within(0.001f));
    }

    /// <summary>治疗吃源侧治疗强度（+ HealPower）再乘连携；DamageDealtScale 不参与治疗。</summary>
    [Test]
    public void Direct_heal_adds_source_heal_power_then_chain_multiplier()
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            effects: new Dictionary<string, EffectDto>
            {
                ["mend"] = new() { Id = "mend", Kind = EEffectKind.Heal, Params = new() { ["amount"] = 10f } },
            });
        var enemy = new EnemyUnit("e0", "slime", maxHp: 100);
        var sim = CombatSimulationTestBuilder.Minimal(enemy, registry, rules: []);
        var executor = new CombatEffectExecutor(registry);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.HealPower, 5f);
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.DamageDealtScale, 1f);
        sim.SetChainBonus(0.5f);
        var source = new CombatTargetRef(ECombatSide.Player, 0);
        var ledger = new CombatTargetRef(ECombatSide.Player, -1);
        // Minimal 阶段账本尚未被 BattleStart 冻结填充（当前 0 会触发败北短路），手工铺一份工作血量。
        sim.PlayerTeam.Asc.SetBaseValue(AttributeIds.MaxHealth, 200f);
        sim.PlayerTeam.Asc.SetBaseValue(AttributeIds.Health, 100f);
        var before = sim.PlayerTeam.SharedHpExact;

        executor.ExecuteEffectRef(new EffectRefDto { EffectId = "mend" }, sim, source, [ledger]);

        // (10 + 5 治疗强度) × (1 + 0.5 连携) = 22.5；100% 增伤不改变治疗量。
        Assert.That(sim.PlayerTeam.SharedHpExact, Is.EqualTo(before + 22.5f).Within(0.001f));
    }

    /// <summary>
    /// 定值伤害通道的返回值必须是<b>真正写进血量</b>的数额：目标解析不出（索引越界）时
    /// 只过规则管线、不写血量，返回值必须为 0——否则普通攻击/充能球的战报会虚报伤害。
    /// </summary>
    [Test]
    public void Fixed_damage_reports_zero_when_the_target_cannot_be_written()
    {
        var registry = CombatTestHelper.CreateFullRegistry();
        var enemy = new EnemyUnit("e0", "slime", maxHp: 50);
        using var sim = CombatSimulationTestBuilder.Minimal(enemy, registry, rules: []);
        var source = new CombatTargetRef(ECombatSide.Player, 0);

        var written = sim.EffectExecutor.ApplyFixedDamage(
            sim,
            source,
            [new CombatTargetRef(ECombatSide.Enemy, 5)],
            12f);

        Assert.That(written, Is.Zero, "越界目标不该被计成已造成的伤害");
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(50f).Within(0.001f), "越界目标不受伤");

        // 正常目标仍照旧累计。
        var hit = sim.EffectExecutor.ApplyFixedDamage(
            sim,
            source,
            [new CombatTargetRef(ECombatSide.Enemy, 0)],
            12f);

        Assert.That(hit, Is.EqualTo(12f).Within(0.001f));
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(38f).Within(0.001f));
    }
}