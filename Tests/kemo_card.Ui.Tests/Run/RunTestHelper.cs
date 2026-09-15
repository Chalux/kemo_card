using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Ui.Tests.Combat;

namespace KemoCard.Ui.Tests.Run;

internal static class RunTestHelper
{
    /// <summary>
    /// 测试用战斗定义 id。Run 进入战斗必须由内容提供真实 <see cref="BattleDto"/>
    /// （<c>RunController.StartBattle</c> 不再硬编码占位敌人）。
    /// </summary>
    public const string TestBattleId = "battle.run_test";

    /// <summary>测试用敌人定义 id。</summary>
    public const string TestEnemyId = "enemy.run_test_slime";

    public static GameDefinitionRegistry CreateRegistryWithHpCard()
    {
        return CombatTestHelper.CreateFullRegistry(
            cards: new Dictionary<string, CardDto>
            {
                [CombatSimulationTestBuilder.PartyHpCardId] = new()
                {
                    Id = CombatSimulationTestBuilder.PartyHpCardId,
                    Stats = new CardStatBlockDto { HpCap = 10 },
                },
            },
            enemies: new Dictionary<string, EnemyDto>
            {
                [TestEnemyId] = new() { Id = TestEnemyId, MaxHp = 10 },
            },
            battles: new Dictionary<string, BattleDto>
            {
                [TestBattleId] = new()
                {
                    Id = TestBattleId,
                    Waves =
                    [
                        new BattleWaveDto
                        {
                            EnemySpawns = [new EnemySpawnDto { EnemyId = TestEnemyId, Count = 1 }],
                        },
                    ],
                },
            });
    }

    public static CharacterInstance CreateCharacterWithHp(int index)
    {
        return new CharacterInstance(
            new CharacterDto
            {
                Id = $"hero_{index}",
                Cards = [CombatSimulationTestBuilder.PartyHpCardId],
            },
            $"inst-{index}");
    }
}