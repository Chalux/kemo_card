using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 敌人 <c>buffRefs</c> 的挂载时机：BattleStart 与<b>每次换波</b>都必须挂一遍。
/// 换波会把敌人换成全新实例（buff 容器为空），漏挂会让「波 2 起的敌人失去内容声明的开战 buff」
/// 静默发生——单波内容（如训练木桩）完全测不出来。
/// </summary>
[TestFixture]
public sealed class EnemyWaveBuffTests
{
    private const string EnemyId = "wave_dummy";
    private const string BuffId = "wave_dummy.regen";

    [Test]
    public void Battle_start_attaches_declared_enemy_buffs()
    {
        using var sim = BuildTwoWaveBattle();

        sim.RunBattleStart();

        Assert.That(sim.EnemyTeam.Enemies[0].Buffs.Find(BuffId), Is.Not.Null, "开战时必须挂上 buffRefs");
    }

    [Test]
    public void Advancing_to_the_next_wave_reattaches_declared_enemy_buffs()
    {
        using var sim = BuildTwoWaveBattle();
        sim.RunBattleStart();
        var firstWaveEnemy = sim.EnemyTeam.Enemies[0];
        Assert.That(firstWaveEnemy.Buffs.Find(BuffId), Is.Not.Null, "前提：第一波已挂载");

        // 清空第一波 → CheckEndConditions 推进到第二波（换成全新敌人实例）。
        foreach (var enemy in sim.EnemyTeam.Enemies)
            enemy.ApplyDamage(enemy.MaxHp);
        sim.CheckEndConditions();

        Assert.That(sim.CurrentWaveIndex, Is.EqualTo(1), "前提：确实换到了第二波");
        Assert.That(sim.TurnsIntoWave, Is.Zero, "换波重置波内回合计数");

        var secondWaveEnemy = sim.EnemyTeam.Enemies[0];
        Assert.That(secondWaveEnemy, Is.Not.SameAs(firstWaveEnemy), "换波是全新实例（容器为空）");
        Assert.That(secondWaveEnemy.Buffs.Find(BuffId), Is.Not.Null, "换波后必须重新挂载 buffRefs");
    }

    private static CombatSimulation BuildTwoWaveBattle()
    {
        var battle = new BattleDto
        {
            Id = "two_wave_battle",
            Waves =
            [
                new BattleWaveDto { EnemySpawns = [new EnemySpawnDto { EnemyId = EnemyId, Count = 1 }] },
                new BattleWaveDto { EnemySpawns = [new EnemySpawnDto { EnemyId = EnemyId, Count = 1 }] },
            ],
        };

        var registry = CombatTestHelper.CreateFullRegistry(
            buffs: new Dictionary<string, BuffDto>
            {
                [BuffId] = new() { Id = BuffId, DurationType = EBuffDurationType.Permanent },
            },
            enemies: new Dictionary<string, EnemyDto>
            {
                [EnemyId] = new()
                {
                    Id = EnemyId,
                    MaxHp = 20,
                    BuffRefs = [new BuffRefDto { BuffId = BuffId }],
                },
            },
            battles: new Dictionary<string, BattleDto> { [battle.Id] = battle });

        var attributes = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 10f,
            [AttributeIds.MaxEnergy] = 0f,
            [AttributeIds.InitialEnergy] = 0f,
        };
        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests($"c{index}", attributes))
            .ToArray();

        var firstWave = CombatSimulationFactory.SpawnWaveEnemies(battle.Waves[0], registry, out var error);
        Assert.That(firstWave, Is.Not.Null, error);

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 40),
            new EnemyTeamState(firstWave!),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.BattleStart,
            battle: battle);
    }
}
