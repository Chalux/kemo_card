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
		var party = CreateParty(count: 4, registry);
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

	public static CombatSimulation WithQueuedCard()
	{
		var card = new CardDto
		{
			Id = "card.hit",
			DisplayNameId = "card.hit",
			TargetSide = ETargetSide.Enemy,
			TargetScope = ETargetScope.Single,
			TargetCount = 1,
			RetargetPolicy = ERetargetPolicy.Default,
			Priority = 1,
			SkillRefs =
			[
				new SkillRefDto { SkillId = "skill.hit" },
			],
		};
		var skill = new SkillDto
		{
			Id = "skill.hit",
			DisplayNameId = "skill.hit",
			DescId = "skill.hit.desc",
			EffectRefs =
			[
				new EffectRefDto { EffectId = "effect.hit" },
			],
		};
		var effect = new EffectDto
		{
			Id = "effect.hit",
			Kind = EEffectKind.Damage,
			Params = new Dictionary<string, object> { ["amount"] = 1 },
		};
		var registry = CombatTestHelper.CreateFullRegistry(
			cards: new Dictionary<string, CardDto> { [card.Id] = card },
			skills: new Dictionary<string, SkillDto> { [skill.Id] = skill },
			effects: new Dictionary<string, EffectDto> { [effect.Id] = effect });

		var attrs = BuildCharacterAttributes();
		var characters = Enumerable.Range(0, 4)
			.Select(i => CharacterBattleInstance.CreateForTests($"c{i}", attrs))
			.ToArray();
		var player = new PlayerTeamState(characters, sharedMaxHp: 40);
		var enemyTeam = new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 10)]);
		var simulation = new CombatSimulation(
			player,
			enemyTeam,
			new CombatRuleEngine([]),
			registry,
			initialPhase: ECombatPhase.CardExecution);
		simulation.CardQueue.Enqueue(new QueuedCardEntry(
			CharacterIndex: 0,
			CardId: card.Id,
			RuntimeInstanceId: "rt-card-hit",
			Priority: card.Priority,
			Targets: [new CombatTargetRef(ECombatSide.Enemy, 0)],
			Sequence: simulation.AllocateQueueSequence()));
		return simulation;
	}

	public static CombatSimulation WithQueuedCardTargetingEnemy(int index)
	{
		var card = new CardDto
		{
			Id = "card.queued",
			DisplayNameId = "card.queued",
			TargetSide = ETargetSide.Enemy,
			TargetScope = ETargetScope.Single,
			TargetCount = 1,
			RetargetPolicy = ERetargetPolicy.Default,
			Priority = 1,
			SkillRefs =
			[
				new SkillRefDto { SkillId = "skill.queued" },
			],
		};
		var queuedSkill = new SkillDto
		{
			Id = "skill.queued",
			DisplayNameId = "skill.queued",
			DescId = "skill.queued.desc",
			EffectRefs =
			[
				new EffectRefDto { EffectId = "effect.queued" },
			],
		};
		var executeSkill = new SkillDto
		{
			Id = "execute",
			DisplayNameId = "execute",
			DescId = "execute.desc",
			EffectRefs =
			[
				new EffectRefDto { EffectId = "effect.execute" },
			],
		};
		var registry = CombatTestHelper.CreateFullRegistry(
			cards: new Dictionary<string, CardDto> { [card.Id] = card },
			skills: new Dictionary<string, SkillDto>
			{
				[queuedSkill.Id] = queuedSkill,
				[executeSkill.Id] = executeSkill,
			},
			effects: new Dictionary<string, EffectDto>
			{
				["effect.queued"] = new()
				{
					Id = "effect.queued",
					Kind = EEffectKind.Damage,
					Params = new Dictionary<string, object> { ["amount"] = 1 },
				},
				["effect.execute"] = new()
				{
					Id = "effect.execute",
					Kind = EEffectKind.Damage,
					Params = new Dictionary<string, object> { ["amount"] = 999 },
				},
			});

		var attrs = BuildCharacterAttributes();
		var characters = Enumerable.Range(0, 4)
			.Select(i => CharacterBattleInstance.CreateForTests($"c{i}", attrs))
			.ToArray();
		var player = new PlayerTeamState(characters, sharedMaxHp: 40);
		var enemyTeam = new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 10)]);
		var simulation = new CombatSimulation(
			player,
			enemyTeam,
			new CombatRuleEngine([]),
			registry,
			initialPhase: ECombatPhase.Player);
		characters[0].SetHasActed(true);
		simulation.CardQueue.Enqueue(new QueuedCardEntry(
			CharacterIndex: 0,
			CardId: card.Id,
			RuntimeInstanceId: "rt-card-queued",
			Priority: card.Priority,
			Targets: [new CombatTargetRef(ECombatSide.Enemy, index)],
			Sequence: simulation.AllocateQueueSequence()));
		return simulation;
	}
}
