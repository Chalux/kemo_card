using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 出货内容里的「木桩练习」：单波两个木桩、10000 血、每回合开始回血、无行动意图。
/// </summary>
[TestFixture]
public sealed class TrainingDummyContentTests
{
    private const string BattleId = "training_dummy";
    private const string EnemyId = "training_dummy";
    private const string RegenerationBuffId = "training_dummy_regeneration";
    private const int MaxHp = 10000;

    [Test]
    public void Battle_is_one_wave_of_two_dummies()
    {
        var definitions = BaseGameContent.Load();

        Assert.That(definitions.Battles.TryGetValue(BattleId, out var battle), Is.True, "缺少木桩战斗");
        Assert.That(battle!.Waves, Has.Count.EqualTo(1), "只有一波");
        Assert.That(battle.Waves[0].EnemySpawns, Has.Count.EqualTo(1));
        Assert.That(battle.Waves[0].EnemySpawns[0].EnemyId, Is.EqualTo(EnemyId));
        Assert.That(battle.Waves[0].EnemySpawns[0].Count, Is.EqualTo(2), "两个木桩");
    }

    [Test]
    public void Dummy_has_10000_hp_and_no_intent()
    {
        var definitions = BaseGameContent.Load();

        Assert.That(definitions.Enemies.TryGetValue(EnemyId, out var dummy), Is.True, "缺少木桩敌人定义");
        Assert.That(dummy!.MaxHp, Is.EqualTo(MaxHp));
        Assert.That(dummy.SkillRefs, Is.Empty, "没有技能 = 没有行动意图");
        Assert.That(
            dummy.BuffRefs.Select(buffRef => buffRef.BuffId),
            Does.Contain(RegenerationBuffId),
            "开局获得回血 buff");
    }

    [Test]
    public void Regeneration_buff_heals_10000_on_turn_start()
    {
        var definitions = BaseGameContent.Load();

        Assert.That(definitions.Buffs.TryGetValue(RegenerationBuffId, out var regeneration), Is.True);
        var hook = regeneration!.Hooks.OnTurnStart.Single();
        Assert.That(definitions.Effects.TryGetValue(hook.EffectId, out var effect), Is.True);

        Assert.That(effect!.Kind, Is.EqualTo(EEffectKind.Heal));
        var amount = effect.Params!["amount"];
        Assert.That(
            Convert.ToInt32(amount.ToString()),
            Is.EqualTo(10000),
            "每回合开始恢复 10000");
    }

    [Test]
    public void Shipped_battle_heals_dummies_and_never_hurts_the_party()
    {
        var registry = BaseGameContent.BuildRegistry();
        using var sim = StartBattle(registry);
        var dummies = sim.EnemyTeam.Enemies;

        Assert.That(dummies, Has.Count.EqualTo(2));
        foreach (var dummy in dummies)
        {
            Assert.That(
                dummy.Buffs.Find(RegenerationBuffId),
                Is.Not.Null,
                "开战即挂上回血 buff（EnemyDto.buffRefs 接线）");
        }

        // 打掉 7000 → 回合开始回满（10000 上限内一次回满）。
        foreach (var dummy in dummies)
            dummy.ApplyDamage(7000);
        var sharedHpBefore = sim.PlayerTeam.SharedHp;

        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase(); // 卡牌执行（空队列）+ 普通攻击
        sim.AdvancePhase(); // 敌方阶段（木桩无技能 → 无行动）

        foreach (var dummy in dummies)
        {
            Assert.That(
                dummy.Asc.GetCurrentValue(AttributeIds.Health),
                Is.EqualTo((float)MaxHp).Within(0.001f),
                "回合开始回满");
            Assert.That(dummy.IntentSkillId, Is.Null, "木桩没有行动意图");
        }

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(sharedHpBefore), "木桩不还手");
    }

    private static CombatSimulation StartBattle(GameDefinitionRegistry registry)
    {
        var battle = registry.Store.Battles[BattleId];
        // 用出货内容里的角色与卡牌组队（ch只做数值占位：木桩不还手，队伍强度不影响断言）。
        var party = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => new CharacterInstance(
                new CharacterDto { Id = "chalux", Cards = ["strike"] },
                $"inst-{index}"))
            .ToArray();
        var sim = CombatSimulationFactory.TryCreate(
            battle,
            party,
            registry,
            new HostRng(20260920, "combat"),
            runSeed: 20260920,
            runRuleIds: null,
            scriptHost: null,
            modId: "base.game",
            CombatRuleCatalog.CreateDefault(),
            out var error);
        Assert.That(sim, Is.Not.Null, $"战斗创建失败：{error}");

        sim!.AdvancePhase(); // BattleStart → 首个玩家阶段
        return sim;
    }
}
