using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 回合开始的节奏与敌人开战 buff 的接线（2026-09-20）：
/// 一次"回合开始"每回合只发生一次（历史上"进入敌方阶段"会额外补发一次，导致 onTurnStart 翻倍）；
/// 敌人内容里的 <c>buffRefs</c> 在开战时挂到敌人自己身上。
/// </summary>
[TestFixture]
public sealed class TurnStartCadenceTests
{
    [Test]
    public void Turn_start_hook_fires_exactly_once_per_round()
    {
        // 钩子效果：对持有者造成 1 点伤害 → 用敌人血量变化计数。
        using var sim = Build(buffOnEnemy: true);
        var enemy = sim.EnemyTeam.Enemies[0];
        var before = enemy.Asc.GetCurrentValue(AttributeIds.Health);

        // 第 1 回合：卡牌执行阶段 →（末尾不再补发回合开始）→ 敌方阶段 → 回合数 +1，回合开始补发一次。
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
        sim.AdvancePhase();
        Assert.That(
            before - enemy.Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(1f).Within(0.001f),
            "第一回合 onTurnStart 只应触发一次");
        Assert.That(sim.TurnNumber, Is.EqualTo(2));

        // 第 2 回合再来一次 → 累计 2 次。
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
        sim.AdvancePhase();
        Assert.That(
            before - enemy.Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(2f).Within(0.001f),
            "第二回合累计只应触发两次（每回合一次）");
    }

    [Test]
    public void Turn_start_hook_fires_once_on_the_battle_start_round()
    {
        // 开战走 RunBattleStart：首个回合开始在 RunBattleStart 里补发一次（不再额外补发）。
        var registry = RegistryWithProbeBuff();
        var enemy = new EnemyUnit("e0", "slime", maxHp: 100);
        using var sim = new CombatSimulation(
            new PlayerTeamState(CreateCharacters(), sharedMaxHp: 100),
            new EnemyTeamState([enemy]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.BattleStart,
            runSeed: 1);
        ApplyProbeBuff(sim, new CombatTargetRef(ECombatSide.Enemy, 0));
        var before = enemy.Asc.GetCurrentValue(AttributeIds.Health);

        sim.AdvancePhase(); // RunBattleStart → 首个玩家阶段（内含一次回合开始）

        Assert.That(
            before - enemy.Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(1f).Within(0.001f),
            "首个回合的回合开始也只触发一次");
    }

    [Test]
    public void Enemy_buffRefs_are_applied_at_battle_start()
    {
        var regeneration = new BuffDto
        {
            Id = "buff.test_regen",
            DurationType = EBuffDurationType.Permanent,
            Hooks = new BuffEffectHooksDto
            {
                OnTurnStart = [new EffectRefDto { EffectId = "effect.test_heal" }],
            },
        };
        var registry = CombatTestHelper.CreateFullRegistry(
            enemies: new Dictionary<string, EnemyDto>
            {
                ["dummy"] = new()
                {
                    Id = "dummy",
                    MaxHp = 1000,
                    SkillRefs = [],
                    BuffRefs = [new BuffRefDto { BuffId = regeneration.Id }],
                },
            },
            buffs: new Dictionary<string, BuffDto> { [regeneration.Id] = regeneration },
            effects: new Dictionary<string, EffectDto>
            {
                ["effect.test_heal"] = new()
                {
                    Id = "effect.test_heal",
                    Kind = EEffectKind.Heal,
                    Params = new Dictionary<string, object> { ["amount"] = 400 },
                },
            });
        var dummy = new EnemyUnit("e0", "dummy", maxHp: 1000);
        using var sim = new CombatSimulation(
            new PlayerTeamState(CreateCharacters(), sharedMaxHp: 100),
            new EnemyTeamState([dummy]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.BattleStart,
            runSeed: 1);

        sim.AdvancePhase(); // RunBattleStart：挂敌人开战 buff → 首个回合开始

        Assert.That(dummy.Buffs.Find(regeneration.Id), Is.Not.Null, "敌人开战必须挂上 buffRefs 声明的 buff");

        // 掉血 → 再走一轮 → 每回合开始回一次 400（且只回一次）。
        dummy.ApplyDamage(700);
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
        sim.AdvancePhase();

        Assert.That(
            dummy.Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(700f).Within(0.001f),
            "300 + 400 = 700（每回合只回一次）");
    }

    [Test]
    public void Enemy_without_skills_has_no_intent_and_does_not_act()
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            enemies: new Dictionary<string, EnemyDto>
            {
                ["dummy"] = new() { Id = "dummy", MaxHp = 500, SkillRefs = [] },
            });
        var dummy = new EnemyUnit("e0", "dummy", maxHp: 500);
        using var sim = new CombatSimulation(
            new PlayerTeamState(CreateCharacters(), sharedMaxHp: 100),
            new EnemyTeamState([dummy]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.BattleStart,
            runSeed: 1);

        sim.AdvancePhase(); // BattleStart
        var sharedHpBefore = sim.PlayerTeam.SharedHp;
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase(); // 卡牌执行（空）
        sim.AdvancePhase(); // 敌方阶段

        Assert.That(dummy.IntentSkillId, Is.Null, "无技能 = 无行动意图");
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(sharedHpBefore), "无意图的敌人不会造成任何伤害");
    }

    #region 装配

    private static CombatSimulation Build(bool buffOnEnemy)
    {
        var registry = RegistryWithProbeBuff();
        var enemy = new EnemyUnit("e0", "slime", maxHp: 100);
        var sim = new CombatSimulation(
            new PlayerTeamState(CreateCharacters(), sharedMaxHp: 100),
            new EnemyTeamState([enemy]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 1);

        if (buffOnEnemy)
            ApplyProbeBuff(sim, new CombatTargetRef(ECombatSide.Enemy, 0));

        return sim;
    }

    private static GameDefinitionRegistry RegistryWithProbeBuff() => CombatTestHelper.CreateFullRegistry(
        buffs: new Dictionary<string, BuffDto>
        {
            ["buff.probe"] = new()
            {
                Id = "buff.probe",
                DurationType = EBuffDurationType.Permanent,
                Hooks = new BuffEffectHooksDto
                {
                    OnTurnStart = [new EffectRefDto { EffectId = "effect.probe" }],
                },
            },
        },
        effects: new Dictionary<string, EffectDto>
        {
            ["effect.probe"] = new()
            {
                Id = "effect.probe",
                Kind = EEffectKind.Damage,
                Params = new Dictionary<string, object> { ["amount"] = 1 },
            },
        });

    private static void ApplyProbeBuff(CombatSimulation sim, CombatTargetRef holder)
    {
        var result = sim.Buffs.Apply(sim, holder, "buff.probe");
        Assert.That(result.Success, Is.True, result.Error);
    }

    private static CharacterBattleInstance[] CreateCharacters() =>
        [.. Enumerable.Range(0, CombatConstants.SlotCount).Select(index =>
            CharacterBattleInstance.CreateForTests(
                $"c{index}",
                new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    [AttributeIds.MaxHealth] = 50f,
                    // 攻击力 0：普通攻击不产生伤害，避免污染钩子计数。
                    [AttributeIds.PhysicalAttack] = 0f,
                    [AttributeIds.MagicAttack] = 0f,
                }))];

    #endregion
}
