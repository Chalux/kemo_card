using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Rules.Builtin;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Ui.Tests.Combat;

internal static class CombatSimulationTestBuilder
{
    internal const string PartyHpCardId = "test.party_hp";

    private static IReadOnlyDictionary<string, float> BuildCharacterAttributes(
        float maxHealth = 10f,
        float maxEnergy = 0f,
        float initialEnergy = 0f)
    {
        return new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = maxHealth,
            [AttributeIds.MaxEnergy] = maxEnergy,
            [AttributeIds.InitialEnergy] = initialEnergy,
        };
    }

    public static IReadOnlyList<CharacterInstance> CreateParty(int count, GameDefinitionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        var cardIds = registry.Store.TryGetCard(PartyHpCardId, out _)
            ? new List<string> { PartyHpCardId }
            : [];

        return Enumerable.Range(0, count)
            .Select(index => new CharacterInstance(new CharacterDto
            {
                Id = $"party_{index}",
                Cards = cardIds,
            }))
            .ToArray();
    }

    public static CombatSimulation Minimal(
        EnemyUnit enemy,
        GameDefinitionRegistry registry,
        IEnumerable<ICombatRule>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(enemy);
        ArgumentNullException.ThrowIfNull(registry);

        var attrs = BuildCharacterAttributes();
        var player = new PlayerTeamState(
            [CharacterBattleInstance.CreateForTests("c0", attrs)],
            sharedMaxHp: 10);
        var enemyTeam = new EnemyTeamState([enemy]);
        var ruleEngine = new CombatRuleEngine(rules ?? []);
        return new CombatSimulation(player, enemyTeam, ruleEngine, registry);
    }

    public static CombatSimulation Standard() => FullBattle(seed: 1);

    public static CombatSimulation FullBattle(int seed)
    {
        var battle = new BattleDto
        {
            Id = "test_battle",
            CombatRuleIds =
            [
                SharedHpDefeatRule.RuleId,
                AllEnemiesDefeatedVictoryRule.RuleId,
            ],
            Waves =
            [
                new BattleWaveDto
                {
                    EnemySpawns = [new EnemySpawnDto { EnemyId = "slime", Count = 1 }],
                },
            ],
        };
        var registry = CombatTestHelper.CreateFullRegistry(
            cards: new Dictionary<string, CardDto>
            {
                [PartyHpCardId] = new()
                {
                    Id = PartyHpCardId,
                    Stats = new CardStatBlockDto { HpCap = 10 },
                },
            },
            enemies: new Dictionary<string, EnemyDto> { ["slime"] = new() { Id = "slime", MaxHp = 10 } },
            battles: new Dictionary<string, BattleDto> { [battle.Id] = battle });
        var catalog = CombatRuleCatalog.CreateDefault();
        var party = CreateParty(count: CombatConstants.SlotCount, registry);
        var sim = CombatSimulationFactory.TryCreate(
            battle,
            party,
            registry,
            new HostRng(seed, "combat"),
            runSeed: seed,
            runRuleIds: null,
            scriptHost: null,
            modId: "test.mod",
            catalog,
            out var error);
        if (sim is null)
            throw new InvalidOperationException(error ?? "FullBattle 战斗模拟创建失败。");

        sim.TransitionTo(ECombatPhase.Player);
        sim.Marble.BeginTurn(sim.TurnNumber, sim.PlayerTeam.Characters.Count);
        return sim;
    }

    public static CombatSimulation StandardPlayerPhase()
    {
        var registry = CombatTestHelper.CreateFullRegistry();
        var attrs = BuildCharacterAttributes();
        var characters = Enumerable.Range(0, 4)
            .Select(i => CharacterBattleInstance.CreateForTests($"c{i}", attrs))
            .ToArray();
        var player = new PlayerTeamState(characters, sharedMaxHp: 40);
        var enemyTeam = new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 10)]);
        var ruleEngine = new CombatRuleEngine([]);
        return new CombatSimulation(player, enemyTeam, ruleEngine, registry, initialPhase: ECombatPhase.Player);
    }

    internal const string MarkableCardId = "test.markable";

    /// <summary>
    /// 玩家阶段：4 名角色，每人手上抓好 <paramref name="handSize"/> 张 <see cref="MarkableCardId"/>，
    /// 可用能量已灌满，供「标记入队」相关测试使用。
    /// </summary>
    public static CombatSimulation PlayerPhaseWithHand(
        ECostType costType = ECostType.Energy,
        int cost = 1,
        int energy = 5,
        int handSize = 3, int characterCount = 4, int deckSize = 5)
    {
        var registry = new GameDefinitionRegistry();
        RebuildMarkableContent(registry, costType, cost);

        var attrs = BuildCharacterAttributes(maxEnergy: energy, initialEnergy: energy);
        var characters = Enumerable.Range(0, characterCount)
            .Select(i => CharacterBattleInstance.CreateForTests(
                $"c{i}",
                attrs,
                Enumerable.Range(0, deckSize)
                    .Select(slot => new CardRuntimeEntry(MarkableCardId, $"rt-c{i}-{slot}"))))
            .ToArray();
        foreach (var character in characters)
        {
            character.DrawCards(handSize);
            character.RefillAvailableEnergy();
        }

        var player = new PlayerTeamState(characters, sharedMaxHp: 40);
        var enemyTeam = new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 100)]);
        return new CombatSimulation(
            player,
            enemyTeam,
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player);
    }

    private static void RebuildMarkableContent(
        GameDefinitionRegistry registry,
        ECostType costType,
        int cost)
    {
        var card = new CardDto
        {
            Id = MarkableCardId,
            DisplayNameId = MarkableCardId,
            CostType = costType,
            Cost = cost,
            TargetSide = ETargetSide.Enemy,
            TargetScope = ETargetScope.Single,
            TargetCount = 1,
            RetargetPolicy = ERetargetPolicy.Default,
            SkillRefs = [new SkillRefDto { SkillId = "skill.markable" }],
        };
        CombatTestHelper.RebuildInto(
            registry,
            cards: new Dictionary<string, CardDto> { [card.Id] = card },
            skills: new Dictionary<string, SkillDto>
            {
                ["skill.markable"] = new()
                {
                    Id = "skill.markable",
                    EffectRefs = [new EffectRefDto { EffectId = "effect.markable" }],
                },
            },
            effects: new Dictionary<string, EffectDto>
            {
                ["effect.markable"] = new()
                {
                    Id = "effect.markable",
                    Kind = EEffectKind.Damage,
                    Params = new Dictionary<string, object> { ["amount"] = 1 },
                },
            });
    }

}