using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 出货内容里的木桩：静默回血沙包「木桩练习」，以及两只**会还手**的流程验证木桩——
/// 必杀木桩（10000 血 / 随机单体 10000 物理，验证失败流程）与轻击木桩（100 血 / 随机单体 50 物理）。
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

    #region 会还手的流程木桩

    [TestCase("training_dummy_fatal", 10000, 10000)]
    [TestCase("training_dummy_light", 100, 50)]
    public void Striking_dummies_match_the_design(string battleId, int maxHp, int amount)
    {
        var definitions = BaseGameContent.Load();

        Assert.That(definitions.Battles.TryGetValue(battleId, out var battle), Is.True, $"缺少战斗 {battleId}");
        Assert.That(battle!.Waves, Has.Count.EqualTo(1), "只有一波");
        Assert.That(battle.Waves[0].EnemySpawns, Has.Count.EqualTo(1));
        Assert.That(battle.Waves[0].EnemySpawns[0].EnemyId, Is.EqualTo(battleId));
        Assert.That(battle.Waves[0].EnemySpawns[0].Count, Is.EqualTo(1), "单只");

        Assert.That(definitions.Enemies.TryGetValue(battleId, out var dummy), Is.True, "缺少木桩敌人定义");
        Assert.That(dummy!.MaxHp, Is.EqualTo(maxHp));
        Assert.That(dummy.SkillRefs, Is.Not.Empty, "会还手：必须有技能");

        var skillRef = dummy.SkillRefs.Single();
        Assert.That(definitions.Skills.TryGetValue(skillRef.SkillId, out var skill), Is.True);
        var spec = skill!.TargetOverride;
        Assert.That(spec, Is.Not.Null, "必须声明目标，否则只会打队伍账本");
        Assert.That(spec!.Side, Is.EqualTo(ETargetSide.Enemy), "敌方视角的 Enemy = 玩家侧");
        Assert.That(spec.Scope, Is.EqualTo(ETargetScope.RandomN), "随机");
        Assert.That(spec.TargetCount, Is.EqualTo(1), "单体");

        var actionRef = skill.ActionRefs.Single();
        Assert.That(
            actionRef.Params?["Amount"]?.ToString(),
            Is.EqualTo(amount.ToString()),
            "固定伤害值");
        Assert.That(definitions.SkillActions.TryGetValue(actionRef.ActionId, out var action), Is.True);
        Assert.That(action!.Kind, Is.EqualTo(ESkillActionKind.ApplyGameplayEffect));
        Assert.That(
            action.Params?["gameplayEffectId"]?.ToString(),
            Is.EqualTo("training_dummy_strike_damage"));

        var gameplayEffect = definitions.GameplayEffects["training_dummy_strike_damage"];
        Assert.That(gameplayEffect.Executions, Has.Count.EqualTo(1));
        Assert.That(gameplayEffect.Executions[0].DamageType, Is.EqualTo("Physical"));
        Assert.That(gameplayEffect.Executions[0].AttackScale, Is.Zero, "固定伤害：不吃自身攻击力");
    }

    [Test]
    public void Fatal_dummy_defeats_the_party_on_the_first_enemy_phase()
    {
        var registry = BaseGameContent.BuildRegistry();
        using var sim = StartBattle(registry, "training_dummy_fatal");

        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase(); // 卡牌执行（空队列）+ 普通攻击
        sim.AdvancePhase(); // 敌方阶段：随机单体 10000 点物理 → 共享血量账本归零

        Assert.That(sim.PlayerTeam.IsDefeated, Is.True, "10000 点伤害必定击穿队伍共享血量");
        Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Defeat), "失败流程：相位进入 Defeat");
    }

    [Test]
    public void Light_dummy_strikes_one_random_character_each_turn()
    {
        var registry = BaseGameContent.BuildRegistry();
        using var sim = StartBattle(registry, "training_dummy_light");
        var hpBefore = sim.PlayerTeam.SharedHp;

        sim.Presentation.Clear();
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase(); // 卡牌执行 + 普通攻击（打掉木桩若干血，不致命）
        sim.AdvancePhase(); // 敌方阶段：随机单体 50 点物理

        var hits = sim.Presentation.Drain()
            .OfType<DamageDealtEvent>()
            .Where(evt => evt.Source.Side == ECombatSide.Enemy && evt.Target.Side == ECombatSide.Player)
            .ToArray();
        Assert.That(hits, Has.Length.EqualTo(1), "每回合只打随机单体一击");
        Assert.That(hits[0].Target.Index, Is.InRange(0, CombatConstants.SlotCount - 1), "命中一个玩家槽位");
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hpBefore - 50), "50 点物理（队伍物防 0）");
    }

    #endregion

    private static CombatSimulation StartBattle(GameDefinitionRegistry registry, string battleId = BattleId)
    {
        var battle = registry.Store.Battles[battleId];
        // 用出货内容里的角色与卡牌组队（ch只做数值占位：木桩不还手，队伍强度不影响断言）。
        // 卡牌必须真实存在于内容里：牌面 stats 决定队伍共享 HP 上限，牌 id 悬空会让战斗直接建不起来
        // （占位卡 strike / strike_plus 已于 2026-09-21 删除，这里改用 chalux 的专属卡）。
        var party = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => new CharacterInstance(
                new CharacterDto { Id = "chalux", Cards = ["chalux_orca_ice_rush"] },
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
